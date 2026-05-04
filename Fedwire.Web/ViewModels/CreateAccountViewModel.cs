public class CreateAccountViewModel
{
    public string AccountNumber {get;set;} = "";
    public string RoutingNumber {get;set;} = "";
    public decimal Balance {get;set;}
    public string Currency {get;set;} = "USD";
}