using System.Xml.Linq;
using Microsoft.VisualBasic;

public class Iso20022Validator
{
    public bool isValid = false;  
    public IsoValidationResult Validate(string xml)
    {
        var result = new IsoValidationResult();

        if(string.IsNullOrWhiteSpace(xml))
        {
            result.Errors.Add(new IsoValidationError
            {
                Code = "EMPTY_XML",
                Message = "XML is empty"
            });
            return result;
        }

        XDocument doc;

        try
        {
            doc = XDocument.Parse(xml);
        }
        catch
        {
            result.Errors.Add(new IsoValidationError
            {
                Code= "INVALID_XML",
                Message = "Malformed XML"
            });
            return result;
        }

        var txId = doc.Descendants("TxId").FirstOrDefault()?.Value;
        var amount = doc.Descendants("IntrBkSttlmAmt").FirstOrDefault()?.Value;
        var debtorAccount = doc.Descendants("DbtrAcct").Descendants("Id").FirstOrDefault()?.Value;

        if(string.IsNullOrWhiteSpace(txId))
            result.Errors.Add(new IsoValidationError
            {
                Code = "MISSING_TXID",
                Message = "TxId is required",
                Field = "TxId"
            });
        if(string.IsNullOrWhiteSpace(amount))
            result.Errors.Add(new IsoValidationError{
                Code = "MISSING_AMOUNT",
                Message = "IntrBkSttlmAmt is required",
                Field = "IntrBkSttlmAmt"
        });
        if(string.IsNullOrWhiteSpace(debtorAccount))
            result.Errors.Add(new IsoValidationError{
                Code = "MISSING_AMOUNT",
                Message = "IntrBkSttlmAmt is required",
                Field = "IntrBkSttlmAmt"
        });

        if(!decimal.TryParse(amount, out _))
            result.Errors.Add(new IsoValidationError{
                Code = "MISSING_AMOUNT",
                Message = "IntrBkSttlmAmt is required",
                Field = "IntrBkSttlmAmt"
        });


        return result;
    }
}