using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.Data.SqlClient;
using System.Xml.Linq;
using System.Text;

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

        var consumer = new EventingBasicConsumer(_channel);

        consumer.Received += async (model, ea) =>
        {
            var xml = Encoding.UTF8.GetString(ea.Body.ToArray());
            var correlationId = Guid.NewGuid().ToString();

            _logger.LogInformation("📩 Wire received");

            var parsed = ParseIso(xml);

            await ProcessTransaction(parsed, xml, correlationId);
        };

        _channel.BasicConsume(
            queue: "wire.inbound.queue",
            autoAck: true,
            consumer: consumer);

        return Task.Delay(Timeout.Infinite, stoppingToken);
    }

    // -------------------------------
    // ISO PARSING
    // -------------------------------
    private (string TxId, decimal Amount, string Currency, bool IsValid) ParseIso(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);

            var txId = doc.Descendants("TxId").FirstOrDefault()?.Value;
            var amountStr = doc.Descendants("IntrBkSttlmAmt").FirstOrDefault()?.Value;

            var amount = decimal.TryParse(amountStr, out var a) ? a : 0;

            var isValid =
                !string.IsNullOrWhiteSpace(txId) &&
                amount > 0;

            return (txId ?? Guid.NewGuid().ToString(), amount, "USD", isValid);
        }
        catch
        {
            return (Guid.NewGuid().ToString(), 0, "USD", false);
        }
    }

    // -------------------------------
    // MAIN PROCESSING PIPELINE
    // -------------------------------
    private async Task ProcessTransaction(
        (string TxId, decimal Amount, string Currency, bool IsValid) msg,
        string xml,
        string correlationId)
    {
        var status = msg.IsValid ? "ACSC" : "RJCT";

        var connStr = _config.GetConnectionString("WireDb");

        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        // -------------------------------
        // 1. UPSERT WIRE TRANSACTION
        // -------------------------------
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

        // -------------------------------
        // 2. GET WireTransactionId
        // -------------------------------
        var cmdGetId = new SqlCommand(@"
SELECT WireTransactionId 
FROM WireTransactions 
WHERE ClientReferenceId = @TxId", conn);

        cmdGetId.Parameters.AddWithValue("@TxId", msg.TxId);

        var wireId = (Guid)await cmdGetId.ExecuteScalarAsync();

        // -------------------------------
        // 3. INSERT ISO MESSAGE (AUDIT)
        // -------------------------------
        var cmdIso = new SqlCommand(@"
INSERT INTO IsoMessages
(WireTransactionId, MessageType, Direction, CorrelationId, MessageXml)
VALUES
(@WireId, 'pacs.008', 'Inbound', @CorrelationId, @Xml)", conn);

        cmdIso.Parameters.AddWithValue("@WireId", wireId);
        cmdIso.Parameters.AddWithValue("@CorrelationId", correlationId);
        cmdIso.Parameters.AddWithValue("@Xml", xml);

        await cmdIso.ExecuteNonQueryAsync();

        // -------------------------------
        // 4. STATUS HISTORY
        // -------------------------------
        var cmdHist = new SqlCommand(@"
INSERT INTO WireStatusHistory
(WireTransactionId, OldStatus, NewStatus, ChangedBy)
VALUES
(@WireId, NULL, @Status, 'SYSTEM')", conn);

        cmdHist.Parameters.AddWithValue("@WireId", wireId);
        cmdHist.Parameters.AddWithValue("@Status", status);

        await cmdHist.ExecuteNonQueryAsync();

        // -------------------------------
        // 5. PROCESSING LOG
        // -------------------------------
        var cmdLog = new SqlCommand(@"
INSERT INTO ProcessingLogs
(WireTransactionId, StepName, Status, Details)
VALUES
(@WireId, 'ISO_PROCESS', @Status, @Details)", conn);

        cmdLog.Parameters.AddWithValue("@WireId", wireId);
        cmdLog.Parameters.AddWithValue("@Status", "Success");
        cmdLog.Parameters.AddWithValue("@Details", $"Processed TxId {msg.TxId}");

        await cmdLog.ExecuteNonQueryAsync();

        // -------------------------------
        // 6. RESPONSE (FUTURE STEP)
        // -------------------------------
        _logger.LogInformation($"Processed wire {msg.TxId} → {status}");
    }
}