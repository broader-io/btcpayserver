using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using NBXplorer;

namespace BTCPayServer.Plugins.BSC.Services;

public abstract class BSCBasePollingService : BSCBaseHostedService
{
    private TimeSpan _pollInterval;

    readonly CompositeDisposable _leases = new();

    protected BSCBasePollingService(
        int chainId,
        TimeSpan pollInterval,
        BTCPayNetworkProvider btcPayNetworkProvider,
        SettingsRepository settingsRepository,
        EventAggregator eventAggregator,
        Logs logs
    ) : base(
        chainId,
        eventAggregator,
        settingsRepository,
        btcPayNetworkProvider,
        logs)
    {
        _pollInterval = pollInterval;
    }

    protected virtual Task PollingCallback(BSCBTCPayNetwork network, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected virtual Task StartPoller(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken);

        await StartPoller(cancellationToken);

        foreach (BSCBTCPayNetwork network in _networks)
        {
            var listenPoller = new Timer(
                _ => PollingCallback(network, cancellationToken),
                null,
                0,
                (int)_pollInterval.TotalMilliseconds);
            _leases.Add(listenPoller);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _leases.Dispose();
        return Task.CompletedTask;
    }
}
