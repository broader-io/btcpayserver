#if ALTCOINS
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.ProsusPay
{
    public class ProsusPayConfiguration
    {
        public static string SettingsKey()
        {
            return "BTCPayServer.Plugins.ProsusPay.ProsusPayConfiguration";
        }

        [Display(Name = "Crypto Addresses")]
        public List<CryptoCodeConfiguration> CoinConfiguration { get; set; } = new();
    }

    public class CryptoCodeConfiguration
    {
        public string cryptoCode { get; set; }
        public string address { get; set; }
        public decimal minPaymentAmount { get; set; }

        // Fee charged to the payment gateway vendor
        public decimal feeRate { get; set; }

        // Fee to be paid for the payout transaction
        public decimal networkFee { get; set; }
    }
}
#endif
