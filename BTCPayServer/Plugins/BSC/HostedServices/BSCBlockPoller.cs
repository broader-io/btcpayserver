using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;

namespace BTCPayServer.Plugins.BSC.Services;

public class BSCBlockPoller : BSCBasePollingService
{

    private BigInteger _previousBlock;
    
    public BSCBlockPoller(
        int chainId, 
        TimeSpan pollInterval, 
        BTCPayNetworkProvider btcPayNetworkProvider, 
        SettingsRepository settingsRepository, 
        EventAggregator eventAggregator, 
        Logs logs) : 
        base(chainId, pollInterval, btcPayNetworkProvider, settingsRepository, eventAggregator, logs)
    {
    }

    protected override async Task PollingCallback(BSCBTCPayNetwork network, CancellationToken cancellationToken)
    {
        var currentBlock = (await _web3.GetLatestBlockNumber()).BlockNumber.Value;
        
        while (_previousBlock <= currentBlock)
        {
            _previousBlock++;

            EventAggregator.Publish(new NewBlockEvent
            {
                chainId = _chainId,
                blockParameter = new BlockParameter(new HexBigInteger(_previousBlock))
            });
        }
    }

    protected override async Task StartPoller(CancellationToken cancellationToken)
    {
        _previousBlock = _settings.GetLastSeenBlockNumber().BlockNumber.Value;
        if (_previousBlock == 0)
        {
            var currentBlock = (await _web3.GetLatestBlockNumber()).BlockNumber.Value;
            await _web3.UpdateLastSeenBlockNumberSettings((long)currentBlock);
            _previousBlock = currentBlock;
        }
    }

    public class NewBlockEvent
    {
        public int chainId;
        public BlockParameter blockParameter;
    }
}
