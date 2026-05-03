public class IsoMessage
{
    public Guid IsoMessageId {get;set;}
    public Guid WireTransactionId {get;set;}
    public string MessageType {get;set;} = "";
    public string Direction {get;set;} = "";
    public string CorrelationId {get;set;} = "";
    public string MessageXml {get;set;} = "";
    public DateTime CreatedAt {get;set;}
    public WireTransaction WireTransaction {get;set;}  = null!;
}