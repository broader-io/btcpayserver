namespace BTCPayServer
{
    public class BSCBTCPayNetwork : BTCPayNetwork
    {
        public int ChainId { get; set; }
        public int CoinType { get; set; }

        public string PaymentMethodId;

        public string GetDefaultKeyPath()
        {
            return $"m/44'/{CoinType}'/0'";
        }
    }

    public class BEP20BTCPayNetwork : BSCBTCPayNetwork
    {
        public string SmartContractAddress { get; set; }
    }
}
