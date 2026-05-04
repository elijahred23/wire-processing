public class IsoValidationResult
{
    public bool IsValid {get;set;}
    public List<string> Errors {get;set;} = new();
}