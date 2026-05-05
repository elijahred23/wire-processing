public class IsoValidationResult
{
    public bool IsValid => Errors.Count == 0; 
    public List<IsoValidationError> Errors {get;set;} = new();
}

public class IsoValidationError
{
    public string Code {get;set; } = "";
    public string Message {get;set;} = "";
    public string? Field {get;set;} 
}