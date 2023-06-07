using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using NBitcoin;

namespace BTCPayServer.Plugins.ProsusPay
{
    public class ProsusPayPaymentHelper
    {
        public static BitcoinWitScriptAddress GetInputAddress(Transaction transaction, BTCPayNetwork network)
        {
            var _WitScriptId = new WitScriptId(transaction.Inputs[0].WitScript);
            return new BitcoinWitScriptAddress(_WitScriptId, network.NBitcoinNetwork);
        }

        public static decimal GetFiatAmount(
            PaymentEntity paymentEntity, 
            InvoiceEntity invoiceEntity)
        {
            int precision = paymentEntity.Network.Divisibility;
            var value = paymentEntity.GetCryptoPaymentData().GetValue();
            var paymentMethods = invoiceEntity.GetPaymentMethods();
            var fromRate = paymentMethods[paymentEntity.GetPaymentMethodId()].Rate;
            return fromRate * decimal.Round(value, precision);
        }

        public static PaymentMethod GetPaymentMethod(InvoiceEntity invoiceEntity, string cryptoCode)
        {
            return invoiceEntity
                .GetPaymentMethods()
                .First(pm => pm.GetId().CryptoCode == cryptoCode);
        }

        // public static decimal ToSatoshi(decimal amount, InvoiceEntity invoiceEntity, string cryptoCode)
        // {
        //     var paymentMethod = GetPaymentMethod(invoiceEntity, cryptoCode);
        //     
        // }

        /**
         * Calculates the amount to be paid to the vendor based off of the amount due
         * for a specific crypto code.
         */
        public static decimal GetVendorPaymentAmount(
            string cryptoCode,
            InvoiceEntity invoiceEntity,
            PaymentEntity paymentEntity,
            SettingsRepository settingsRepository
            )
        {
            var paymentMethod = GetPaymentMethod(invoiceEntity, cryptoCode);
            var divisibility = paymentMethod.Network.Divisibility;
            var divisor = (decimal)Math.Pow(10, divisibility);
            var accounting = paymentMethod.Calculate();
            //var satoshi = accounting.Due.Satoshi;
            //var dueForThisCryptoCode = satoshi / (decimal) Math.Pow(10, precision);
            var paymentValue = paymentEntity.GetCryptoPaymentData().GetValue();
            //var overpaid = accounting.OverpaidHelper.Satoshi;
            var dueUncapped = accounting.DueUncapped.Satoshi / divisor;
            var dueForThisCryptoCode = Math.Max(dueUncapped + paymentValue, 0);
            var amount = Math.Min(paymentValue, dueForThisCryptoCode);
            
            var feeRate = GetCodeConfiguration(settingsRepository, cryptoCode).Result.feeRate;
            return decimal.Round(amount, divisibility) * feeRate;
        }

        public static async Task<CryptoCodeConfiguration> GetCodeConfiguration(
            SettingsRepository settingsRepository,
            string cryptoCode
        )
        {
            var settings = await settingsRepository.GetSettingAsync<ProsusPayConfiguration>(ProsusPayConfiguration.SettingsKey());
            return settings.CoinConfiguration.First(c => c.cryptoCode == cryptoCode);
        }

    }
}
