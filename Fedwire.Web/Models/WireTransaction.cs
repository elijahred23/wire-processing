public class WireTransaction
{
    public Guid WireTransactionId {get;set;}
    public string ClientReferenceId {get;set;} = "";
    public decimal Amount {get;set;} 
    public string CurrencyCode {get;set;} = "";
    public string Status {get;set;} = "";
    public string Direction {get;set;} = "";
    public DateTime CreatedAt {get;set;}
    public List<IsoMessage> IsoMessages {get;set;} = new();
}