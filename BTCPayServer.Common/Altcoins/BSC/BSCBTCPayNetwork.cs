
namespace BTCPayServer
{
    public class BSCBTCPayNetwork : BTCPayNetworkBase
    {
        public int ChainId { get; set; }
        public int CoinType { get; set; }

        public string GetDefaultKeyPath()
        {
            return $"m/44'/{CoinType}'/0'/0/x";
        }
    }

    public class BEP20BTCPayNetwork : BSCBTCPayNetwork
    {
        public string SmartContractAddress { get; set; }
    }

}

