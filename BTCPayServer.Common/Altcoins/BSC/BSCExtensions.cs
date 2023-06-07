#if ALTCOINS
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer
{
    public static class BSCExtensions
    {
        
        public static IEnumerable<string> GetAllBSCSubChains(this BTCPayNetworkProvider networkProvider, BTCPayNetworkProvider unfiltered)
        {
            var ethBased = networkProvider.GetAll().OfType<BSCBTCPayNetwork>();
            var chainId = ethBased.Select(network => network.ChainId).Distinct();
            return unfiltered.GetAll().OfType<BSCBTCPayNetwork>()
                .Where(network => chainId.Contains(network.ChainId))
                .Select(network => network.CryptoCode.ToUpperInvariant());
        }
    }
}
#endif
