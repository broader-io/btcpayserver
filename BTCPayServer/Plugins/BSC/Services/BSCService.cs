#if ALTCOINS
using System;
using System.Collections.Generic;
using System.Globalization;
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
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace BTCPayServer.Plugins.BSC.Services
{
    public class BSCService : EventHostedServiceBase
    {
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly SettingsRepository _settingsRepository;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly IConfiguration _configuration;
        private readonly PaymentService _paymentService;

        private readonly Dictionary<int, Dictionary<Type, BSCBaseHostedService>> _chainHostedServices =
            new Dictionary<int, Dictionary<Type, BSCBaseHostedService>>();

        private readonly Web3Wrapper _web3Wrapper;

        private readonly Dictionary<int, CancellationTokenSource> _chainHostedServiceCancellationTokenSources =
            new Dictionary<int, CancellationTokenSource>();

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
            _configuration = configuration;
            _paymentService = paymentService;
        }

        private async Task<List<BSCBaseHostedService>> InstantiateServicesForChain(int chainId)
        {
            var services = new List<BSCBaseHostedService>();

            var settings = await GetSettings(chainId);

            try
            {

                // var bscWatcher = new BSCInvoiceWatcher(
                //     chainId,
                //     _btcPayNetworkProvider,
                //     _settingsRepository,
                //     EventAggregator,
                //     _invoiceRepository,
                //     _paymentService,
                //     Logs
                // );
                //services.Add(bscWatcher);

                // var bscBalancePollingService = new BSCBalancePollingService(
                //     chainId,
                //     settings.GetHighPriorityPollingPeriod(),
                //     _btcPayNetworkProvider,
                //     _invoiceRepository,
                //     _settingsRepository,
                //     EventAggregator,
                //     Logs
                // );
                // services.Add(bscBalancePollingService);

                var bscTransactionPollerService = new BSCBEP20TransactionPollerService(
                    chainId,
                    settings.GetHighPriorityPollingPeriod(),
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
            var a = store.GetSupportedPaymentMethods(_btcPayNetworkProvider);
            var BSCSupportedPaymentMethod = store.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<BSCSupportedPaymentMethod>()
                .SingleOrDefault(method =>
                    method.PaymentId.CryptoCode.ToUpperInvariant() == reserveBSCAddress.CryptoCode.ToUpperInvariant());
            if (BSCSupportedPaymentMethod == null)
            {
                EventAggregator.Publish(new ReserveBSCAddressResponse() {OpId = reserveBSCAddress.OpId, Failed = true});
                return;
            }

            BSCSupportedPaymentMethod.CurrentIndex++;
            var address = BSCSupportedPaymentMethod.GetDerivedAddress()?
                .Invoke(BSCSupportedPaymentMethod.CurrentIndex);

            if (string.IsNullOrEmpty(address))
            {
                EventAggregator.Publish(new ReserveBSCAddressResponse() {OpId = reserveBSCAddress.OpId, Failed = true});
                return;
            }

            store.SetSupportedPaymentMethod(BSCSupportedPaymentMethod.PaymentId,
                BSCSupportedPaymentMethod);
            await _storeRepository.UpdateStore(store);
            EventAggregator.Publish(new ReserveBSCAddressResponse()
            {
                Address = address,
                Index = BSCSupportedPaymentMethod.CurrentIndex,
                CryptoCode = BSCSupportedPaymentMethod.CryptoCode,
                OpId = reserveBSCAddress.OpId,
                StoreId = reserveBSCAddress.StoreId,
                XPub = BSCSupportedPaymentMethod.AccountDerivation
            });
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
