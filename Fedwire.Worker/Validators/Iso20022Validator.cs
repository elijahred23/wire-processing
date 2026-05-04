using System.Xml.Linq;

public class Iso20022Validator
{
    public IsoValidationResult Validate(string xml)
    {
        var result = new IsoValidationResult();

        if(string.IsNullOrWhiteSpace(xml))
        {
            result.Errors.Add("XML is empty");
            return result;
        }

        XDocument doc;

        try
        {
            doc = XDocument.Parse(xml);
        }
        catch
        {
            result.Errors.Add("Invalid XML format");
            return result;
        }

        var txId = doc.Descendants("TxId").FirstOrDefault()?.Value;
        var amount = doc.Descendants("IntrBkSttlmAmt").FirstOrDefault()?.Value;
        var debtorAccount = doc.Descendants("DbtrAcct").Descendants("Id").FirstOrDefault()?.Value;

        if(string.IsNullOrWhiteSpace(txId))
            result.Errors.Add("Missing TxId");
        if(string.IsNullOrWhiteSpace(amount))
            result.Errors.Add("Missing Amount");
        if(string.IsNullOrWhiteSpace(debtorAccount))
            result.Errors.Add("Missing Debtor Account");

        if(!decimal.TryParse(amount, out _))
            result.Errors.Add("Invalid Amount format");

        result.IsValid = result.Errors.Count == 0;

        return result;
    }
}