using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Plugins.BSC.Extensions;


public static class InvoiceExtensions
{
    public static async Task<InvoiceEntity> GetInvoiceFromAddress(
        string address, 
        PaymentMethodId paymentMethodId,
        InvoiceRepository invoiceRepository)
    {
        var key = address + "#" + paymentMethodId.ToString().ToUpperInvariant();
        return (await invoiceRepository.GetInvoicesFromAddresses(new[] {key}))
            .FirstOrDefault();
    }
}
