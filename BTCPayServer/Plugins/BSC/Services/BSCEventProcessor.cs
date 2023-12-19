using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BTCPayServer.Logging;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.BSC.Services
{
    public class BSCEventProcessor : EventHostedServiceBase
    {
        private List<string> Addresses;

        private readonly EventAggregator _eventAggregator;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly SettingsRepository _settingsRepository;
        private readonly IConfiguration _configuration;
        private readonly InvoiceRepository _invoiceRepository;

        private readonly HashSet<PaymentMethodId> PaymentMethods;
        private readonly List<BSCBTCPayNetwork> Networks;
        private int ChainId { get; }

        private readonly Dictionary<int, CancellationTokenSource> _chainHostedServiceCancellationTokenSources =
            new Dictionary<int, CancellationTokenSource>();

        public BSCEventProcessor(
            int chainId,
            EventAggregator eventAggregator,
            BTCPayNetworkProvider btcPayNetworkProvider,
            SettingsRepository settingsRepository,
            IConfiguration configuration,
            InvoiceRepository invoiceRepository,
            Logs logs) : base(eventAggregator, logs)
        {
            _eventAggregator = eventAggregator;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _settingsRepository = settingsRepository;
            _configuration = configuration;
            _invoiceRepository = invoiceRepository;

            ChainId = chainId;

            Networks = btcPayNetworkProvider.GetAll()
                .OfType<BSCBTCPayNetwork>()
                .Where(network => network.ChainId == chainId)
                .ToList();
            PaymentMethods = Networks
                .Select(network => new PaymentMethodId(network.CryptoCode, BSCPaymentType.Instance))
                .ToHashSet();
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await base.StartAsync(cancellationToken);
            _ = Task.Run(async () =>
            {
                LoadAddresses();
                while (!cancellationToken.IsCancellationRequested)
                {
                    // _eventAggregator.Publish(new CheckWatchers());
                    // await Task.Delay(IsAllAvailable() ? TimeSpan.FromDays(1) : TimeSpan.FromSeconds(5),
                    //     cancellationToken);
                }
            }, cancellationToken);
        }
        
        private async Task FetchTransactions(){}

        private async Task LoadAddresses()
        {
            var invoices = await _invoiceRepository.GetPendingInvoices();
            invoices = invoices
                .Where(entity =>
                    PaymentMethods.Any(id =>
                        entity.GetPaymentMethod(id)?.GetPaymentMethodDetails()?.Activated is true))
                .ToArray();

            foreach (var network in Networks)
            {
                var paymentMethodId = new PaymentMethodId(network.CryptoCode, BSCPaymentType.Instance);
                Addresses = invoices
                    .Select(entity => (
                        Invoice: entity,
                        PaymentMethodDetails: entity.GetPaymentMethods().TryGet(paymentMethodId),
                        Address: entity.GetPaymentMethods().TryGet(paymentMethodId)
                            .GetPaymentMethodDetails().GetPaymentDestination()
                    )).Where(tuple => tuple.PaymentMethodDetails?.GetPaymentMethodDetails()?.Activated is true)
                    .Select(row => row.Address)
                    .ToList();
            }
        }
    }
}
