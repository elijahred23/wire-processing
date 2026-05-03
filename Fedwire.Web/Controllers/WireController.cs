using System.Text;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

public class WireController: Controller
{
    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    public IActionResult Submit(WireRequestViewModel model)
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
            <IntrBkSttlmAmt>{model.Amount}</IntrBkSttlmAmt>
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

        return View("Index");
    }
}