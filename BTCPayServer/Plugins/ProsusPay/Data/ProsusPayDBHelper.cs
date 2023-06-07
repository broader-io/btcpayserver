using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.ProsusPay
{
    public class ProsusPayDBHelper
    {
        private readonly ProsusPayDbContextFactory _ProsusPayDbContextFactory;

        public ProsusPayDBHelper(
            ProsusPayDbContextFactory prosusPayDbContextFactory)
        {
            _ProsusPayDbContextFactory = prosusPayDbContextFactory;
        }

        // public async Task<ProsusPayPaymentsData> GetProsusPayPayment(string paymentDataId)
        // {
        //     await using var ctx = _ProsusPayDbContextFactory.CreateContext();
        //     return ctx.ProsusPayPayments
        //         .First(p => p.PaymentDataId == paymentDataId);
        // }

        public async Task<List<ProsusPayPaymentsData>> GetProsusPayPayments(string invoiceId)
        {
            await using var ctx = _ProsusPayDbContextFactory.CreateContext();
            return ctx.ProsusPayPayments
                .Where(p => p.InvoiceDataId == invoiceId)
                .ToList();
        }

        /**
         * Returns the ProsusPayPayments associated to a storeId in the given status
         */
        public async Task<List<ProsusPayPaymentsData>> GetProsusPayPaymentsByStatus(string StoreId,
            ProsusPayPaymentsStatus status)
        {
            await using var ctx = _ProsusPayDbContextFactory.CreateContext();
            return ctx.ProsusPayPayments
                .Where(p => p.StoreDataId == StoreId && p.Status == status)
                .ToList();
        }

        /**
         * Adds a ProsusPayPaymentsData row only if there is no entry for the passed invoice
         */
        public async Task AddProsusPayPayments(ProsusPayPaymentsData PPPaymentsData, string InvoiceId)
        {
            await using var ctx = _ProsusPayDbContextFactory.CreateContext();
            var exists = await ctx.ProsusPayPayments.AnyAsync(p =>
                p.InvoiceDataId == InvoiceId
            );
            if (!exists)
            {
                await ctx.ProsusPayPayments.AddAsync(PPPaymentsData);
            }
        }
    }
}
