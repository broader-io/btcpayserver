using System;

namespace BTCPayServer.Plugins.ProsusPay.Exceptions;

public class ProsusPayPayoutException : Exception
{
    public ProsusPayPayoutException(CreatePayoutResult _result)
    {
        Result = _result;
    }

    public CreatePayoutResult Result { get; set; }

    public enum CreatePayoutResult
    {
        Ok,
        Duplicate,
        Expired,
        Archived,
        NotStarted,
        Overdraft,
        AmountTooLow,
        PaymentMethodNotSupported,
    }

    public string GetMessage()
    {
        switch (Result)
        {
            case CreatePayoutResult.Ok:
                return "Payout created successfully";
            case CreatePayoutResult.Duplicate:
                return "This address is already used for another payout";
            case CreatePayoutResult.AmountTooLow:
                return "The requested payout amount is too low";
            case CreatePayoutResult.PaymentMethodNotSupported:
                return "This payment method is not supported by the pull payment";
            default:
                throw new NotSupportedException("Unsupported ClaimResult");
        }
    }
}
