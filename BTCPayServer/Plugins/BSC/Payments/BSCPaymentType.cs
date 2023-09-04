#if ALTCOINS
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.BSC;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer
{
    public class BSCPaymentType: PaymentType
    {
        public static BSCPaymentType Instance { get; } = new BSCPaymentType();
        public override string ToPrettyString() => "On-Chain";

        public override string GetId()=> "BSC";


        public override CryptoPaymentData DeserializePaymentData(BTCPayNetworkBase network, string str)
        {
            return JsonConvert.DeserializeObject<BSCPaymentData>(str);
        }

        public override string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData)
        {
            return JsonConvert.SerializeObject(paymentData);
        }

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(BTCPayNetworkBase network, string str)
        {
            return JsonConvert.DeserializeObject<BSCOnChainPaymentMethodDetails>(str);
        }

        public override string SerializePaymentMethodDetails(BTCPayNetworkBase network, IPaymentMethodDetails details)
        {
            return JsonConvert.SerializeObject(details);
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network, JToken value)
        {
            return JsonConvert.DeserializeObject<BSCSupportedPaymentMethod>(value.ToString());
        }

        public override string GetTransactionLink(BTCPayNetworkBase network, string txId)
        {
            return string.Format(CultureInfo.InvariantCulture, network.BlockExplorerLink, txId);
        }

        public override string GetPaymentLink(BTCPayNetworkBase network, InvoiceEntity invoice, IPaymentMethodDetails paymentMethodDetails,
            decimal cryptoInfoDue, string serverUri)
        {
            throw new System.NotImplementedException();
        }

        public override string InvoiceViewPaymentPartialName { get; }= "Plugins/BSC/Views/Shared/ViewBSCPaymentData";
        public override object GetGreenfieldData(ISupportedPaymentMethod supportedPaymentMethod, bool canModifyStore)
        {
            if (supportedPaymentMethod is BSCSupportedPaymentMethod BSCSupportedPaymentMethod)
            {
                return new
                {
                    BSCSupportedPaymentMethod.XPub
                    //no clue what all those properties saved are and don't care.
                };
            }

            return null;
        }

        public override void PopulateCryptoInfo(InvoiceEntity invoice, PaymentMethod details, InvoiceCryptoInfo invoiceCryptoInfo,
            string serverUrl)
        {
            
        }
    }
}
#endif
