using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.BSC.Configuration;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;
using NBXplorer;

namespace BTCPayServer.Plugins.BSC.Services;

public abstract class BSCBaseHostedService : EventHostedServiceBase
{
    private TimeSpan ReloadWeb3SettingsPeriod = TimeSpan.FromSeconds(60);
    private TimeSpan ReloadSettingsEventPeriod = TimeSpan.FromSeconds(60);

    protected readonly int _chainId;
    protected readonly SettingsRepository _settingsRepository;
    protected BSCConfiguration _settings;
    private readonly string _serviceName;
    protected Web3Wrapper _web3;
    public string _globalError;
    readonly CompositeDisposable _leases = new();

    protected readonly List<BSCBTCPayNetwork> _networks;


    private CancellationTokenSource _cts;

    protected BSCBaseHostedService(
        int chainId,
        EventAggregator eventAggregator,
        SettingsRepository settingsRepository,
        BTCPayNetworkProvider btcPayNetworkProvider,
        Logs logs
    ) : base(eventAggregator, logs)
    {
        _chainId = chainId;
        _serviceName = this.GetType().Name;
        _settingsRepository = settingsRepository;

        _networks = btcPayNetworkProvider.GetAll()
            .OfType<BSCBTCPayNetwork>()
            .Where(network => network.ChainId == chainId)
            .ToList();
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        Logs.PayServer.LogDebug($"Starting {_serviceName} for chain {_chainId}");

        await base.StartAsync(cancellationToken);

        await ReloadSettings();

        _web3 = await Web3Wrapper.GetInstance(_chainId, _settingsRepository);

        await _web3.EnsureChain(_chainId);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        StartLoop();
    }

    private void StartLoop()
    {
        var reloadWeb3ConfigPoller = new Timer(
            _ => EventAggregator.Publish(new ReloadWeb3ConfigurationEvent()),
            null,
            0,
            (int)ReloadWeb3SettingsPeriod.TotalMilliseconds);
        _leases.Add(reloadWeb3ConfigPoller);

        var reloadSettingsPoller = new Timer(
            _ => EventAggregator.Publish(new ReloadSettingsEvent()),
            null,
            0,
            (int)ReloadSettingsEventPeriod.TotalMilliseconds);
        _leases.Add(reloadSettingsPoller);
    }


    protected override void SubscribeToEvents()
    {
        Subscribe<ReloadWeb3ConfigurationEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Logs.PayServer.LogDebug($"Processing event {evt.ToString()}");

        if (evt is ReloadWeb3ConfigurationEvent)
        {
            await _web3.Reload();
        }
        else if (evt is ReloadSettingsEvent)
        {
            await ReloadSettings();
        }
    }

    private async Task ReloadSettings()
    {
        _settings = await BSCConfiguration.GetInstance(
            _settingsRepository,
            _chainId,
            true);
    }
}

public abstract class BSCEvent
{
    public string GetName()
    {
        return this.GetType().Name;
    }
}

public class ReloadWeb3ConfigurationEvent : BSCEvent
{
}

public class ReloadSettingsEvent : BSCEvent
{
}
