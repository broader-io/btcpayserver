#if ALTCOINS
using System;
using Nethereum.RPC.Eth.DTOs;

namespace BTCPayServer.Plugins.BSC.Controllers
{
    public class BSCPaymentViewModel
    {
        public string Crypto { get; set; }
        public long Confirmations { get; set; }
        public string DepositAddress { get; set; }
        public string Amount { get; set; }
        public DateTimeOffset ReceivedTime { get; set; }
        public BlockParameter BlockParameter { get; set; }
        public string BalanceLink { get; set; }
        public bool Replaced { get; set; }
        public long Index { get; set; }
    }
}
#endif
