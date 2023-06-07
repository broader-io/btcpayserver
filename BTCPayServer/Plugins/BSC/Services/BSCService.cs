#if ALTCOINS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.BSC.Configuration;
using BTCPayServer.Plugins.BSC.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace BTCPayServer.Plugins.BSC.Services
{
    public class BSCService : EventHostedServiceBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly EventAggregator _eventAggregator;
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly SettingsRepository _settingsRepository;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly IConfiguration _configuration;
        private readonly PaymentService _paymentService;
        private readonly Dictionary<int, BSCWatcher> _chainHostedServices = new Dictionary<int, BSCWatcher>();

        private readonly Dictionary<int, CancellationTokenSource> _chainHostedServiceCancellationTokenSources =
            new Dictionary<int, CancellationTokenSource>();

        public BSCService(
            IHttpClientFactory httpClientFactory,
            EventAggregator eventAggregator,
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            SettingsRepository settingsRepository,
            InvoiceRepository invoiceRepository,
            IConfiguration configuration,
            PaymentService paymentService,
            Logs logs) : base(eventAggregator, logs)
        {
            _httpClientFactory = httpClientFactory;
            _eventAggregator = eventAggregator;
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _settingsRepository = settingsRepository;
            _invoiceRepository = invoiceRepository;
            _configuration = configuration;
            _paymentService = paymentService;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            var chainIds = _btcPayNetworkProvider.GetAll().OfType<BSCBTCPayNetwork>()
                .Select(network => network.ChainId).Distinct().ToList();
            if (!chainIds.Any())
            {
                return;
            }

            await base.StartAsync(cancellationToken);
            _ = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    _eventAggregator.Publish(new CheckWatchers());
                    await Task.Delay(IsAllAvailable() ? TimeSpan.FromDays(1) : TimeSpan.FromSeconds(5),
                        cancellationToken);
                }
            }, cancellationToken);
        }

        private static bool First = true;

        private async Task LoopThroughChainWatchers(CancellationToken cancellationToken)
        {
            var chainIds = _btcPayNetworkProvider.GetAll().OfType<BSCBTCPayNetwork>()
                .Select(network => network.ChainId).Distinct().ToList();
            foreach (var chainId in chainIds)
            {
                try
                {
                    var settings = await _settingsRepository.GetSettingAsync<BSCConfiguration>(
                        BSCConfiguration.SettingsKey(chainId));
                    if (settings is null || string.IsNullOrEmpty(settings.Web3ProviderUrl))
                    {
                        var val = _configuration.GetValue<string>($"chain{chainId}_web3", null);
                        var valUser = _configuration.GetValue<string>($"chain{chainId}_web3_user", null);
                        var valPass = _configuration.GetValue<string>($"chain{chainId}_web3_password", null);
                        if (val != null && First)
                        {
                            Logs.PayServer.LogInformation($"Setting eth chain {chainId} web3 to {val}");
                            settings ??= new BSCConfiguration()
                            {
                                ChainId = chainId,
                                Web3ProviderUrl = val,
                                Web3ProviderPassword = valPass,
                                Web3ProviderUsername = valUser
                            };
                            await _settingsRepository.UpdateSetting(settings,
                                BSCConfiguration.SettingsKey(chainId));
                        }
                    }

                    var currentlyRunning = _chainHostedServices.ContainsKey(chainId);
                    if (!currentlyRunning)
                    {
                        await HandleChainWatcher(settings, cancellationToken);
                    }
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            First = false;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var chainHostedService in _chainHostedServices.Values)
            {
                chainHostedService.StopAsync(cancellationToken);
            }

            return base.StopAsync(cancellationToken);
        }

        protected override void SubscribeToEvents()
        {
            //base.SubscribeToEvents();

            //_eventAggregator.Subscribe<ReserveBSCAddress>(HandleReserveNextAddress);

            Subscribe<ReserveBSCAddress>();
            Subscribe<SettingsChanged<BSCConfiguration>>();
            Subscribe<CheckWatchers>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is ReserveBSCAddress reserveBSCAddress)
            {
                await HandleReserveNextAddress(reserveBSCAddress);
            }

            if (evt is SettingsChanged<BSCConfiguration> settingsChangedBSCConfig)
            {
                await HandleChainWatcher(settingsChangedBSCConfig.Settings, cancellationToken);
            }

            if (evt is CheckWatchers)
            {
                await LoopThroughChainWatchers(cancellationToken);
            }

            await base.ProcessEvent(evt, cancellationToken);
        }

        private async Task HandleChainWatcher(BSCConfiguration BSCConfiguration,
            CancellationToken cancellationToken)
        {
            if (BSCConfiguration is null)
            {
                return;
            }

            if (_chainHostedServiceCancellationTokenSources.ContainsKey(BSCConfiguration.ChainId))
            {
                _chainHostedServiceCancellationTokenSources[BSCConfiguration.ChainId].Cancel();
                _chainHostedServiceCancellationTokenSources.Remove(BSCConfiguration.ChainId);
            }

            if (_chainHostedServices.ContainsKey(BSCConfiguration.ChainId))
            {
                //await _chainHostedServices[BSCConfiguration.ChainId].StopAsync(cancellationToken);
                _chainHostedServices[BSCConfiguration.ChainId].StopAsync(cancellationToken);
                _chainHostedServices.Remove(BSCConfiguration.ChainId);
            }

            if (!string.IsNullOrWhiteSpace(BSCConfiguration.Web3ProviderUrl))
            {
                var cts = new CancellationTokenSource();
                _chainHostedServiceCancellationTokenSources.AddOrReplace(BSCConfiguration.ChainId, cts);
                _chainHostedServices.AddOrReplace(BSCConfiguration.ChainId,
                    new BSCWatcher(BSCConfiguration.ChainId, BSCConfiguration,
                        _btcPayNetworkProvider, _eventAggregator, _invoiceRepository, _paymentService, Logs));
                await _chainHostedServices[BSCConfiguration.ChainId].StartAsync(CancellationTokenSource
                    .CreateLinkedTokenSource(cancellationToken, cts.Token).Token);
            }
        }

        private async Task HandleReserveNextAddress(ReserveBSCAddress reserveBSCAddress)
        {
            var store = await _storeRepository.FindStore(reserveBSCAddress.StoreId);
            var a = store.GetSupportedPaymentMethods(_btcPayNetworkProvider);
            var BSCSupportedPaymentMethod = store.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<BSCSupportedPaymentMethod>()
                .SingleOrDefault(method => 
                    method.PaymentId.CryptoCode.ToUpperInvariant() == reserveBSCAddress.CryptoCode.ToUpperInvariant());
            if (BSCSupportedPaymentMethod == null)
            {
                _eventAggregator.Publish(new ReserveBSCAddressResponse()
                {
                    OpId = reserveBSCAddress.OpId, Failed = true
                });
                return;
            }

            BSCSupportedPaymentMethod.CurrentIndex++;
            var address = BSCSupportedPaymentMethod.GetDerivedAddress()?
                .Invoke((int)BSCSupportedPaymentMethod.CurrentIndex);

            if (string.IsNullOrEmpty(address))
            {
                _eventAggregator.Publish(new ReserveBSCAddressResponse()
                {
                    OpId = reserveBSCAddress.OpId, Failed = true
                });
                return;
            }
            store.SetSupportedPaymentMethod(BSCSupportedPaymentMethod.PaymentId,
                BSCSupportedPaymentMethod);
            await _storeRepository.UpdateStore(store);
            _eventAggregator.Publish(new ReserveBSCAddressResponse()
            {
                Address = address,
                Index = BSCSupportedPaymentMethod.CurrentIndex,
                CryptoCode = BSCSupportedPaymentMethod.CryptoCode,
                OpId = reserveBSCAddress.OpId,
                StoreId = reserveBSCAddress.StoreId,
                XPub = BSCSupportedPaymentMethod.XPub
            });
        }

        public async Task<ReserveBSCAddressResponse> ReserveNextAddress(ReserveBSCAddress address)
        {
            address.OpId = string.IsNullOrEmpty(address.OpId) ? Guid.NewGuid().ToString() : address.OpId;
            var tcs = new TaskCompletionSource<ReserveBSCAddressResponse>();
            var subscription = _eventAggregator.Subscribe<ReserveBSCAddressResponse>(response =>
            {
                if (response.OpId == address.OpId)
                {
                    tcs.SetResult(response);
                }
            });
            _eventAggregator.Publish(address);

            if (tcs.Task.Wait(TimeSpan.FromSeconds(60)))
            {
                subscription?.Dispose();
                return await tcs.Task;
            }

            subscription?.Dispose();
            return null;
        }

        public class CheckWatchers
        {
            public override string ToString()
            {
                return "";
            }
        }

        public class ReserveBSCAddressResponse
        {
            public string StoreId { get; set; }
            public string CryptoCode { get; set; }
            public string Address { get; set; }
            public long Index { get; set; }
            public string OpId { get; set; }
            public string XPub { get; set; }
            public bool Failed { get; set; }

            public override string ToString()
            {
                return $"Reserved {CryptoCode} address {Address} for store {StoreId}";
            }
        }

        public class ReserveBSCAddress
        {
            public string StoreId { get; set; }
            public string CryptoCode { get; set; }
            public string OpId { get; set; }

            public override string ToString()
            {
                return $"Reserving {CryptoCode} address for store {StoreId}";
            }
        }

        public bool IsAllAvailable()
        {
            return _btcPayNetworkProvider.GetAll().OfType<BSCBTCPayNetwork>()
                .All(network => IsAvailable(network.CryptoCode, out _));
        }

        public bool IsAvailable(string networkCryptoCode, out string error)
        {
            error = null;
            var chainId = _btcPayNetworkProvider.GetNetwork<BSCBTCPayNetwork>(networkCryptoCode)?.ChainId;
            if (chainId != null && _chainHostedServices.TryGetValue(chainId.Value, out var watcher))
            {
                error = watcher.GlobalError;
                return string.IsNullOrEmpty(watcher.GlobalError);
            }
            return false;
        }
    }
}
#endif
