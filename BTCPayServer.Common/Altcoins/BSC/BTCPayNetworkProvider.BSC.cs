#if ALTCOINS
using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitBNB()
        {
            Add(new BSCBTCPayNetwork()
            {
                CryptoCode = "BNB",
                DisplayName = "BNB",
                DefaultRateRules = new[] {"wPROSUS_X = wPROSUS_BTC * BTC_X", "wPROSUS_BTC = wPROSUS(wPROSUS_BTC)",},
                BlockExplorerLink =
                    NetworkType == ChainName.Mainnet
                        ? "https://bscscan.com/{0}"
                        : "https://testnet.bscscan.com/{0}",
                CryptoImagePath = "/imlegacy/wPROSUS.png",
                ShowSyncSummary = true,
                CoinType = NetworkType == ChainName.Mainnet ? 714 : 1,
                ChainId = NetworkType == ChainName.Mainnet ? 56 : 97,
                Divisibility = 18,
            });
        }

        public void InitwPROSUS()
        {
            if (NetworkType != ChainName.Mainnet)
            {
                // Add(new BEP20BTCPayNetwork()
                // {
                //     CryptoCode = "wPROSUS",
                //     DisplayName = "wPROSUS",
                //     DefaultRateRules =
                //         new[] {"wPROSUS_X = wPROSUS_BTC * BTC_X", "wPROSUS_BTC = wPROSUS(wPROSUS_BTC)",},
                //     BlockExplorerLink =
                //         "https://bscscan.com/token/0x56f86cfa34cf4004736554c2784d59e477589c8c/?a={0}",
                //     CryptoImagePath = "/imlegacy/wPROSUS.png",
                //     ShowSyncSummary = false,
                //     CoinType = 1,
                //     ChainId = 56,
                //     SmartContractAddress = "0x56f86cfa34cf4004736554c2784d59e477589c8c",
                //     Divisibility = 12
                // });
                Add(new BEP20BTCPayNetwork()
                {
                    CryptoCode = "WPROSUS",
                    DisplayName = "wPROSUS Testnet",
                    DefaultRateRules = new[]
                    {
                        "wPROSUS_X = wPROSUS_USDT", 
                        "wPROSUS_USDT = wPROSUS(wPROSUS_USDT)",
                    },
                    BlockExplorerLink =
                        "https://testnet.bscscan.com/token/0xe6F47738F66256b8C230f99852CBfAf96d3C02D4/?a={0}",
                    ShowSyncSummary = false,
                    CoinType = 714,
                    ChainId = 97,
                    SmartContractAddress = "0xe6F47738F66256b8C230f99852CBfAf96d3C02D4",
                    Divisibility = 12,
                    CryptoImagePath = "/imlegacy/wPROSUS.png",
                });
            }
            else
            {
                Add(new BEP20BTCPayNetwork()
                {
                    CryptoCode = "WPROSUS",
                    DisplayName = "wPROSUS",
                    DefaultRateRules =
                        new[]
                        {
                            "wPROSUS_X = wPROSUS_USD", 
                            "wPROSUS_USD = wPROSUS(wPROSUS_USD)",
                        },
                    BlockExplorerLink =
                        "https://bscscan.com/token/0x56f86cfa34cf4004736554c2784d59e477589c8c/?a={0}",
                    CryptoImagePath = "/imlegacy/wPROSUS.png",
                    ShowSyncSummary = false,
                    CoinType = 1,
                    ChainId = 56,
                    SmartContractAddress = "0x56f86cfa34cf4004736554c2784d59e477589c8c",
                    Divisibility = 12
                });
            }
        }
    }
}
#endif
