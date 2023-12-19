#if ALTCOINS
using BTCPayServer.Payments;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.BSC
{
    public class BSCPaymentMethodDetails : IPaymentMethodDetails
    {
        public string KeyPath { get; set; }
        public decimal GetNextNetworkFee()
        {
            return _NetworkFeeRate;
        }

        public bool Activated { get; set; }
        public int NetworkFeeMode { get; set; }
        public decimal NetworkFeeRate { get; set; }
        
        [JsonIgnore]
        public decimal _NetworkFeeRate { get; set; }

        public string DepositAddress { get; set; }
        

        public string GetPaymentDestination()
        {
            return DepositAddress;
        }

        public PaymentType GetPaymentType()
        {
            return BSCPaymentType.Instance;
        }

        public void SetPaymentDetails(IPaymentMethodDetails newPaymentMethodDetails)
        {
            DepositAddress = newPaymentMethodDetails.GetPaymentDestination();
            KeyPath = (newPaymentMethodDetails as BSCPaymentMethodDetails)?.KeyPath;
        }

    }
}
#endif
