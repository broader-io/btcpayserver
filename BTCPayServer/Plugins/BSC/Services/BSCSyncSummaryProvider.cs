#if ALTCOINS
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Plugins.BSC.Services
{
    public class BSCSyncSummaryProvider : ISyncSummaryProvider
    {
        private readonly BSCService _BSCService;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        public BSCSyncSummaryProvider(BSCService BSCService, BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _BSCService = BSCService;
            _btcPayNetworkProvider = btcPayNetworkProvider;
        }

        public bool AllAvailable()
        {
            return _BSCService.IsAllAvailable();
        }

        public string Partial { get; } = "Plugins/BSC/Views/Shared/BSCSyncSummary";
        public IEnumerable<ISyncStatus> GetStatuses()
        {
            return _btcPayNetworkProvider
                .GetAll()
                .OfType<BSCBTCPayNetwork>()
                .Where(network => !(network is BEP20BTCPayNetwork))
                .Select(network => network.CryptoCode).Select(network => new SyncStatus()
                {
                    CryptoCode = network, 
                    Available = _BSCService.IsAvailable(network, out _)
                });
        }

        public class SyncStatus : ISyncStatus
        {
            public string CryptoCode { get; set; }
            public bool Available { get; set; }
        }
    }
}
#endif
