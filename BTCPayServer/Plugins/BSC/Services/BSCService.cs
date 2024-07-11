#if ALTCOINS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.BSC.Configuration;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Configuration;
using NBitcoin;

namespace BTCPayServer.Plugins.BSC.Services
{
    public class BSCService : EventHostedServiceBase
    {
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly SettingsRepository _settingsRepository;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly PaymentService _paymentService;

        private readonly Dictionary<int, Dictionary<Type, BSCBaseHostedService>> _chainHostedServices =
            new();

        private readonly Web3Wrapper _web3Wrapper;

        private readonly Dictionary<int, CancellationTokenSource> _chainHostedServiceCancellationTokenSources =
            new();

        private static Semaphore _addressSemaphore;

        public BSCService(
            EventAggregator eventAggregator,
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            SettingsRepository settingsRepository,
            InvoiceRepository invoiceRepository,
            IConfiguration configuration,
            PaymentService paymentService,
            Logs logs) : base(eventAggregator, logs)
        {
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _settingsRepository = settingsRepository;
            _invoiceRepository = invoiceRepository;
            _paymentService = paymentService;

            _addressSemaphore = new Semaphore(initialCount: 1, maximumCount: 1);
        }

        private async Task<List<BSCBaseHostedService>> InstantiateServicesForChain(int chainId)
        {
            var services = new List<BSCBaseHostedService>();

            var settings = await GetSettings(chainId);

            try
            {

                var bscTransactionPollerService = new BSCBEP20TransactionPollerService(
                    chainId,
                    settings.GetBlockPollingPeriod(),
                    _btcPayNetworkProvider,
                    _invoiceRepository,
                    _settingsRepository,
                    EventAggregator,
                    Logs
                );
                services.Add(bscTransactionPollerService);

                var bscBlockPoller = new BSCBlockPoller(
                    chainId,
                    settings.GetBlockPollingPeriod(),
                    _btcPayNetworkProvider,
                    _settingsRepository,
                    EventAggregator,
                    Logs
                );
                services.Add(bscBlockPoller);

                var bSCPaymentService = new BSCPaymentService(
                    chainId,
                    settings.GetPaymentUpdatePollingPeriod(),
                    _settingsRepository,
                    _paymentService,
                    EventAggregator,
                    _btcPayNetworkProvider,
                    _invoiceRepository,
                    Logs
                );
                services.Add(bSCPaymentService);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return services;
        }

        private void AddChainHostedService(
            int chainId,
            BSCBaseHostedService service)
        {
            if (!_chainHostedServices.ContainsKey(chainId))
            {
                _chainHostedServices.Add(chainId, new Dictionary<Type, BSCBaseHostedService>());
            }

            var services = _chainHostedServices[chainId];
            services.AddOrReplace(service.GetType(), service);
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            var chainIds = _btcPayNetworkProvider
                .GetAll()
                .OfType<BSCBTCPayNetwork>()
                .Select(network => network.ChainId)
                .Distinct()
                .ToList();
            if (!chainIds.Any())
            {
                return;
            }

            await base.StartAsync(cancellationToken);
            _ = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    //EventAggregator.Publish(new RestartChainServicesEvent());

                    await RestartServices(cancellationToken);

                    await Task.Delay(IsAllAvailable() ? TimeSpan.FromDays(1) : TimeSpan.FromSeconds(5),
                        cancellationToken);
                }
            }, cancellationToken);
        }

        private static bool First = true;

        private async Task RestartServices(CancellationToken cancellationToken)
        {
            var chainIds = _btcPayNetworkProvider
                .GetAll()
                .OfType<BSCBTCPayNetwork>()
                .Select(network => network.ChainId)
                .Distinct()
                .ToList();

            foreach (var chainId in chainIds)
            {
                try
                {
                    var currentlyRunning = _chainHostedServices.ContainsKey(chainId);
                    if (!currentlyRunning)
                    {
                        await RestartChainServices(
                            chainId,
                            cancellationToken);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw e;
                }
            }

            First = false;
        }

        private async Task<BSCConfiguration> GetSettings(int chainId)
        {
            return await BSCConfiguration.GetInstance(
                _settingsRepository,
                chainId,
                true);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var chainHostedService in _chainHostedServices.Values)
            {
                // chainHostedService.StopAsync(cancellationToken);
            }

            return base.StopAsync(cancellationToken);
        }

        protected override void SubscribeToEvents()
        {
            //base.SubscribeToEvents();

            //_eventAggregator.Subscribe<ReserveBSCAddress>(HandleReserveNextAddress);

            Subscribe<ReserveBSCAddress>();
            // Subscribe<SettingsChanged<BSCConfiguration>>();
            Subscribe<RestartChainServicesEvent>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is ReserveBSCAddress reserveBSCAddress)
            {
                await HandleReserveNextAddress(reserveBSCAddress);
            }

            if (evt is SettingsChanged<BSCConfiguration> settingsChangedBSCConfig)
            {
                await RestartChainServices(
                    settingsChangedBSCConfig.Settings.ChainId,
                    cancellationToken);
            }

            if (evt is RestartChainServicesEvent)
            {
                await RestartServices(cancellationToken);
            }

            await base.ProcessEvent(evt, cancellationToken);
        }


        //# TODO Return list of tasks

        private async Task RestartChainServices(
            int chainId,
            CancellationToken cancellationToken)
        {
            if (_chainHostedServiceCancellationTokenSources.ContainsKey(chainId))
            {
                _chainHostedServiceCancellationTokenSources[chainId].Cancel();
                _chainHostedServiceCancellationTokenSources.Remove(chainId);
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _chainHostedServiceCancellationTokenSources.AddOrReplace(chainId, cts);

            if (_chainHostedServices.ContainsKey(chainId))
            {
                foreach (BSCBaseHostedService bscBaseHostedService in _chainHostedServices[chainId].Values)
                {
                    await bscBaseHostedService.StopAsync(cancellationToken);
                    _chainHostedServices[chainId].Remove(bscBaseHostedService.GetType());
                }
            }

            var services = await InstantiateServicesForChain(chainId);
            foreach (BSCBaseHostedService bscBaseHostedService in services)
            {
                AddChainHostedService(chainId, bscBaseHostedService);
                try
                {
                    await bscBaseHostedService
                        .StartAsync(cts.Token);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }


        private async Task HandleReserveNextAddress(ReserveBSCAddress reserveBSCAddress)
        {
            var store = await _storeRepository.FindStore(reserveBSCAddress.StoreId);
            var bscSupportedPaymentMethod = store
                .GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<BSCSupportedPaymentMethod>()
                .SingleOrDefault(method =>
                    method.PaymentId.CryptoCode.ToUpperInvariant() == reserveBSCAddress.CryptoCode.ToUpperInvariant());

            if (bscSupportedPaymentMethod == null)
            {
                EventAggregator.Publish(new ReserveBSCAddressResponse() {OpId = reserveBSCAddress.OpId, Failed = true});
                return;
            }

            var index = await GetNextIndex(store, bscSupportedPaymentMethod);

            var address = bscSupportedPaymentMethod.GetDerivedAddress()
                .Invoke(index);

            if (string.IsNullOrEmpty(address))
            {
                EventAggregator.Publish(new ReserveBSCAddressResponse() {OpId = reserveBSCAddress.OpId, Failed = true});
                return;
            }

            EventAggregator.Publish(new ReserveBSCAddressResponse()
            {
                Address = address,
                Index = bscSupportedPaymentMethod.CurrentIndex,
                CryptoCode = bscSupportedPaymentMethod.CryptoCode,
                OpId = reserveBSCAddress.OpId,
                StoreId = reserveBSCAddress.StoreId,
                XPub = bscSupportedPaymentMethod.AccountDerivation
            });
        }

        private async Task<int> GetNextIndex(
            StoreData store,
            BSCSupportedPaymentMethod bscSupportedPaymentMethod)
        {
            _addressSemaphore.WaitOne();
            var index = bscSupportedPaymentMethod.CurrentIndex + 1;
            bscSupportedPaymentMethod.CurrentIndex = index;
            store.SetSupportedPaymentMethod(
                bscSupportedPaymentMethod.PaymentId,
                bscSupportedPaymentMethod);
            await _storeRepository.UpdateStore(store);
            _addressSemaphore.Release();
            return index;
        }

        public async Task<ReserveBSCAddressResponse> ReserveNextAddress(ReserveBSCAddress address)
        {
            address.OpId = string.IsNullOrEmpty(address.OpId) ? Guid.NewGuid().ToString() : address.OpId;
            var tcs = new TaskCompletionSource<ReserveBSCAddressResponse>();
            var subscription = EventAggregator.Subscribe<ReserveBSCAddressResponse>(response =>
            {
                if (response.OpId == address.OpId)
                {
                    tcs.SetResult(response);
                }
            });
            EventAggregator.Publish(address);

            if (tcs.Task.Wait(TimeSpan.FromSeconds(60)))
            {
                subscription?.Dispose();
                return await tcs.Task;
            }

            subscription?.Dispose();
            return null;
        }

        public class RestartChainServicesEvent
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
                //error = watcher.GlobalError;
                //return string.IsNullOrEmpty(watcher.GlobalError);
                return true;
            }

            return false;
        }
    }
}
#endif
