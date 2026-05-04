using System.Text;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

public class WireController: Controller
{
    private WireDbContext _context;
    public WireController(WireDbContext context)
    {
        _context = context;
    }
    public async Task<IActionResult> Index()
    {
        ViewBag.Accounts = await _context.Accounts.ToListAsync();
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Submit(WireRequestViewModel model)
    {
        var factory = new ConnectionFactory
        {
            HostName = "localhost",
            UserName = "admin",
            Password = "admin123"
        };

        using var connection = factory.CreateConnection();

        using var channel = connection.CreateModel();

        var xml = $@"
        <Document>
            <TxId>{model.ClientReferenceId}</TxId>
            <DbtrAcct>{model.DebtorAccountNumber}</DbtrAcct>
            <IntrBkSttlmAmt>{model.Amount}</IntrBkSttlmAmt>
            <Ccy>{model.CurrencyCode}</Ccy>
        </Document>
        ";

        var body = Encoding.UTF8.GetBytes(xml);

        var props = channel.CreateBasicProperties();

        props.CorrelationId = Guid.NewGuid().ToString();


        channel.BasicPublish(
            exchange: "wire.exchange",
            routingKey: "wire.submit",
            basicProperties: props,
            body: body
        );

        ViewBag.Mesasge = "Wire submitted successfully";
        ViewBag.Accounts = await _context.Accounts.ToListAsync();

        return View("Index");
    }
}