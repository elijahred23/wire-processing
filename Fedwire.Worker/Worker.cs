using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.Data.SqlClient;
using System.Xml.Linq;
using System.Text;
using System.Threading.Channels;
using System.Security;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _config;

    private IConnection? _connection;
    private IModel? _channel;

    public Worker(ILogger<Worker> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Fedwire Worker started...");

        var factory = new ConnectionFactory
        {
            HostName = "localhost",
            UserName = "admin",
            Password = "admin123"
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.QueueDeclare(
            queue: "wire.inbound.queue",
            durable: true,
            exclusive: false,
            autoDelete: false);

        _channel.QueueDeclare(
            queue: "wire.dlq.queue",
            durable: true,
            exclusive: false,
            autoDelete: false
        );

        var consumer = new EventingBasicConsumer(_channel);

        consumer.Received += async (model, ea) =>
        {
            var xml = Encoding.UTF8.GetString(ea.Body.ToArray());
            var correlationId = Guid.NewGuid().ToString();
            var retryCount = 0;

            if(ea.BasicProperties?.Headers != null && 
            ea.BasicProperties.Headers.ContainsKey("x-retry"))
            {
                retryCount = Convert.ToInt32(ea.BasicProperties.Headers["x-retry"]);
            }

            try
            {
                _logger.LogInformation($"Message received (Retry={retryCount})");


                var parsed = ParseIso(xml);

                await ProcessTransaction(parsed, xml, correlationId);
                _channel.BasicAck(ea.DeliveryTag, false);
            } catch (Exception ex)
            {
                _logger.LogError(ex, "Proccessing failed");


                HandleFailure(_channel, ea, retryCount);
            }

        };

        _channel.BasicConsume(
            queue: "wire.inbound.queue",
            autoAck: false,
            consumer: consumer);

        return Task.Delay(Timeout.Infinite, stoppingToken);
    }
    private void HandleFailure(IModel channel, BasicDeliverEventArgs ea, int retryCount)
    {
        const int maxRetries = 3;

        if(retryCount < maxRetries)
        {
            var props = channel.CreateBasicProperties();

            props.Headers = ea.BasicProperties.Headers ?? new Dictionary<string, object>();


            props.Headers["x-retry"] = retryCount + 1;

            _logger.LogWarning($"Retrying message (attempt {retryCount + 1})");

            channel.BasicPublish(
                exchange: "",
                routingKey: "wire.inbound.queue",
                basicProperties: props,
                body: ea.Body.ToArray());
        }
        else
        {
            _logger.LogError("Sending to DLQ");

            channel.BasicPublish(
                exchange: "",
                routingKey: "wire.dlq.queue",
                basicProperties: channel.CreateBasicProperties(),
                body: ea.Body.ToArray()
            );
        }
        channel.BasicAck(ea.DeliveryTag, false);
    }

    private (string TxId, decimal Amount, string Currency, string AccountNumber, bool IsValid) ParseIso(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);

            var txId = doc.Descendants("TxId").FirstOrDefault()?.Value;
            var amountStr = doc.Descendants("IntrBkSttlmAmt").FirstOrDefault()?.Value;
            var accountNumber = doc.Descendants("DbtrAcct").FirstOrDefault()?.Value;

            var amount = decimal.TryParse(amountStr, out var a) ? a : 0;

            var isValid =
                !string.IsNullOrWhiteSpace(txId) &&
                amount > 0;

            return (txId ?? Guid.NewGuid().ToString(), amount, "USD", accountNumber, isValid);
        }
        catch
        {
            return (Guid.NewGuid().ToString(), 0, "USD", "0", false);
        }
    }

    private async Task ProcessTransaction(
        (string TxId, decimal Amount, string Currency, string AccountNumber,  bool IsValid) msg,
        string xml,
        string correlationId)
    {
        var status = msg.IsValid ? "ACSC" : "RJCT";

        var connStr = _config.GetConnectionString("WireDb");

        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        if(await IsDuplicateAsync(conn, msg.TxId))
        {
            _logger.LogWarning($"Duplicate wire ignored: {msg.TxId}");
            return;
        }

        var cmd1 = new SqlCommand(@"
IF EXISTS (SELECT 1 FROM WireTransactions WHERE ClientReferenceId = @TxId)
    UPDATE WireTransactions
    SET Amount = @Amount,
        CurrencyCode = @Currency,
        Status = @Status,
        UpdatedAt = SYSUTCDATETIME()
    WHERE ClientReferenceId = @TxId
ELSE
    INSERT INTO WireTransactions
    (ClientReferenceId, Amount, CurrencyCode, Status, Direction)
    VALUES
    (@TxId, @Amount, @Currency, @Status, 'Inbound')", conn);

        cmd1.Parameters.AddWithValue("@TxId", msg.TxId);
        cmd1.Parameters.AddWithValue("@Amount", msg.Amount);
        cmd1.Parameters.AddWithValue("@Currency", msg.Currency);
        cmd1.Parameters.AddWithValue("@Status", status);

        await cmd1.ExecuteNonQueryAsync();

        var cmdGetId = new SqlCommand(@"
SELECT WireTransactionId 
FROM WireTransactions 
WHERE ClientReferenceId = @TxId", conn);

        cmdGetId.Parameters.AddWithValue("@TxId", msg.TxId);

        var wireId = (Guid)await cmdGetId.ExecuteScalarAsync();

        var accountNumber = msg.AccountNumber;
        Console.WriteLine($"Account Number: {accountNumber}");
        var balance = await GetAccountBalance(conn, accountNumber);

        if(balance == null)
        {
            _logger.LogError("Account not found");

            await MarkRejected(conn, wireId, "Account not found");

            PublishResponse(
                _channel,
                msg.TxId,
                "RJCT",
                msg.Amount,
                correlationId,
                "Account not found"
            );

            return;
        }


        var debited = await TryDebitAccount(conn, accountNumber, msg.Amount);

        if(!debited)
        {
            _logger.LogWarning("Insufficient funds");
            await MarkRejected(conn, wireId, "Insufficient funds");

        }

        await SaveIdempotencyKey(conn, msg.TxId, wireId);

        var cmdIso = new SqlCommand(@"
INSERT INTO IsoMessages
(WireTransactionId, MessageType, Direction, CorrelationId, MessageXml)
VALUES
(@WireId, 'pacs.008', 'Inbound', @CorrelationId, @Xml)", conn);

        cmdIso.Parameters.AddWithValue("@WireId", wireId);
        cmdIso.Parameters.AddWithValue("@CorrelationId", correlationId);
        cmdIso.Parameters.AddWithValue("@Xml", xml);

        await cmdIso.ExecuteNonQueryAsync();

        var cmdHist = new SqlCommand(@"
INSERT INTO WireStatusHistory
(WireTransactionId, OldStatus, NewStatus, ChangedBy)
VALUES
(@WireId, NULL, @Status, 'SYSTEM')", conn);

        cmdHist.Parameters.AddWithValue("@WireId", wireId);
        cmdHist.Parameters.AddWithValue("@Status", status);

        await cmdHist.ExecuteNonQueryAsync();

        var cmdLog = new SqlCommand(@"
INSERT INTO ProcessingLogs
(WireTransactionId, StepName, Status, Details)
VALUES
(@WireId, 'ISO_PROCESS', @Status, @Details)", conn);

        cmdLog.Parameters.AddWithValue("@WireId", wireId);
        cmdLog.Parameters.AddWithValue("@Status", "Success");
        cmdLog.Parameters.AddWithValue("@Details", $"Processed TxId {msg.TxId}");

        await cmdLog.ExecuteNonQueryAsync();

        await MarkSettled(conn, wireId);

        PublishResponse(
            _channel!,
            msg.TxId,
            status,
            msg.Amount,
            correlationId
        );
        
        _logger.LogInformation($"Processed wire {msg.TxId} → {status}");
    }

    private void PublishResponse(
        IModel channel,
        string txId,
        string status,
        decimal amount,
        string correlationId,
        string? reason = null)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var statusReasonXml = status == "RJCT" && !string.IsNullOrEmpty(reason)
            ? $@"
            <StsRsnInf>
                <Rsn>
                    <Cd>ERR</Cd>
                </Rsn>
                <AddtlInf>{reason}</AddtlInf>
            </StsRsnInf>"
            : "";

        var responseXml = $@"
    <Document>
        <FIToFIPmtStsRpt>
            <GrpHdr>
                <MsgId>{correlationId}</MsgId>
                <CreDtTm>{timestamp}</CreDtTm>
            </GrpHdr>
            <TxInfAndSts>
                <TxId>{txId}</TxId>
                <TxSts>{status}</TxSts>
                <IntrBkSttlmAmt Ccy=""USD"">{amount:F2}</IntrBkSttlmAmt>
                {statusReasonXml}
            </TxInfAndSts>
        </FIToFIPmtStsRpt>
    </Document>";

        var body = Encoding.UTF8.GetBytes(responseXml);

        var props = channel.CreateBasicProperties();

        props.CorrelationId = correlationId;

        props.Headers ??= new Dictionary<string, object>();
        props.Headers["message_type"] = "pacs.002";

        channel.BasicPublish(
            exchange: "wire.exchange",
            routingKey: "wire.response",
            basicProperties: props,
            body: body
        );

        _logger.LogInformation($"📤 pacs.002 sent → {txId} ({status})");
    }
    private async Task<bool> IsDuplicateAsync(SqlConnection conn, string txId)
    {
        var cmd = new SqlCommand(@"
            SELECT COUNT(1)
            FROM IdempotencyKeys
            WHERE IdempotencyKey = @TxId", conn);

        cmd.Parameters.AddWithValue("@TxId", txId);

        var count = (int)await cmd.ExecuteScalarAsync();

        return count > 0;
    }
    private async Task SaveIdempotencyKey(SqlConnection conn, string txId, Guid wireTransactionId)
    {
        var cmd = new SqlCommand(@"
            INSERT INTO IdempotencyKeys (IdempotencyKey, WireTransactionId, ExpiresAt)
            VALUES (@TxId, @WireId, DATEADD(HOUR, 24, SYSUTCDATETIME()))", conn);

        cmd.Parameters.AddWithValue("@TxId", txId);
        cmd.Parameters.AddWithValue("@WireId", wireTransactionId);

        await cmd.ExecuteNonQueryAsync();
    }
    private async Task<decimal?> GetAccountBalance(SqlConnection conn, string accountNumber)
    {
        var cmd = new SqlCommand(@"
            SELECT Balance
            FROM Accounts
            WHERE AccountNumber = @AccountNumber", conn);

        Console.WriteLine($"Account Number: {accountNumber}");

        cmd.Parameters.AddWithValue("@AccountNumber", accountNumber);


        var result = await cmd.ExecuteScalarAsync();

        return result == null ? null : (decimal) result;
    }
    private async Task<bool> TryDebitAccount(SqlConnection conn, string accountNumber, decimal amount)
    {
        var cmd = new SqlCommand(@"
            UPDATE Accounts
            SET Balance = Balance - @Amount
            WHERE AccountNumber = @AccountNumber
            AND BALANCE >= @Amount", conn);

        cmd.Parameters.AddWithValue("@Amount", amount);
        cmd.Parameters.AddWithValue("@AccountNumber", accountNumber);

        var rows = await cmd.ExecuteNonQueryAsync();

        return rows > 0;
    }
    public async Task MarkRejected(SqlConnection conn, Guid wireId, string reason)
    {
        var cmd = new SqlCommand(@"
            UPDATE WireTransactions
            SET Status = 'RJCT', RejectedAt = SYSUTCDATETIME()
            WHERE WireTransactionId = @Id", conn);
        
        cmd.Parameters.AddWithValue("@Id", wireId);

        await cmd.ExecuteNonQueryAsync();
    }
    private async Task MarkSettled(SqlConnection conn, Guid wireId)
    {
        var cmd = new SqlCommand(@"
            UPDATE WireTransactions
            SET Status = 'ACSC', SettledAt = SYSUTCDATETIME()
            WHERE WireTransactionId = @Id", conn);

        cmd.Parameters.AddWithValue("@Id", wireId);

        await cmd.ExecuteNonQueryAsync();
    }
}