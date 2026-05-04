public class WireRequestViewModel
{
    public string ClientReferenceId {get;set;} = "";
    public string DebtorAccountNumber {get;set;} = "";

    public decimal Amount {get;set;}
    public string CurrencyCode {get;set;} = "USD";
}