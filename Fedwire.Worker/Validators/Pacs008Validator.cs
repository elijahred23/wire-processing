using System.Reflection.Metadata;
using System.Resources;
using System.Security.Permissions;
using System.Xml.Linq;
using Microsoft.VisualBasic;

public class Pacs008Validator : IIsoValidator
{
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

        var amountNode = doc.Descendants("IntrBkSttlmAmt").FirstOrDefault();

        var debtorAccount = doc
            .Descendants("DbtrAcct")
            .Descendants("Id")
            .Descendants("Othr")
            .Descendants("Id")
            .FirstOrDefault()?.Value;

        if(string.IsNullOrWhiteSpace(txId))
        {
            result.Errors.Add(new IsoValidationError
            {
                Code = "MISSING_TXID",
                Message = "TxId is required",
                Field = "TxId"
            });
        }

        if(amountNode == null)
        {
            result.Errors.Add(new IsoValidationError
            {
                Code = "MISSING_AMOUNT",
                Message = "IntrBkSttlmAmt is required",
                Field = "IntrBkSttlmAmt"
            });
        }
        else
        {
            if(!decimal.TryParse(amountNode.Value, out var amt)|| amt <= 0){
                result.Errors.Add(new IsoValidationError
                {
                    Code= "INVALID_AMOUNT",
                    Message = "Amount must be a positive number",
                    Field = "IntrBkSttlmAmt"
                });
            } 

            var ccy = amountNode.Attribute("Ccy")?.Value;

            if (string.IsNullOrWhiteSpace(ccy))
            {
                result.Errors.Add(new IsoValidationError
                {
                    Code= "MISSING_CURRENCY",
                    Message = "Currency (Ccy) is required",
                    Field = "IntrBkSttlmAmt"
                });
            }
        }
        if(string.IsNullOrWhiteSpace(debtorAccount))
        {
            result.Errors.Add(new IsoValidationError
            {

            });
        }

        return result;
    }


}