using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Nethereum.RPC.Eth.DTOs;
using System.Numerics;
using AngleSharp.Common;
using BTCPayServer.Payments;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BSC.Services;

public class BSCBalancePollingService : BSCBasePollingService
{
    private readonly Dictionary<string, InvoiceEntity> _invoices = new();
    private InvoiceRepository _invoiceRepository;
    private readonly HashSet<PaymentMethodId> _paymentMethods;

    public BSCBalancePollingService(
        int chainId,
        TimeSpan pollInterval,
        BTCPayNetworkProvider btcPayNetworkProvider,
        InvoiceRepository invoiceRepository,
        SettingsRepository settingsRepository,
        EventAggregator eventAggregator,
        Logs logs) :
        base(chainId, pollInterval, btcPayNetworkProvider, settingsRepository, eventAggregator, logs)
    {
        _invoiceRepository = invoiceRepository;
        _paymentMethods = _networks
            .Select(network => new PaymentMethodId(network.CryptoCode, BSCPaymentType.Instance))
            .ToHashSet();
    }

    protected override async Task StartPoller(CancellationToken cancellationToken)
    {
        var pendingInvoices = await _invoiceRepository
            .GetPendingInvoices(cancellationToken: cancellationToken);
        pendingInvoices
            .Where(i =>
                i.Status == InvoiceStatusLegacy.New)
            .ToList()
            .ForEach(AddInvoice);
    }

    protected override async Task PollingCallback(BSCBTCPayNetwork network, CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            var paymentMethodId = new PaymentMethodId(network.CryptoCode, BSCPaymentType.Instance);
            var expandedInvoices = _invoices
                .Values
                .Where(invoice =>
                    _paymentMethods
                        .Any(id =>
                            invoice.GetPaymentMethod(id)?.GetPaymentMethodDetails()?.Activated is true)
                )
                .Select(entity => (
                        Invoice: entity,
                        PaymentMethod: entity.GetPaymentMethods().TryGet(paymentMethodId),
                        ExistingPayments: entity.GetPayments(network, true)
                            .Select(paymentEntity => (
                                Payment: paymentEntity,
                                PaymentData: (BSCPaymentData)paymentEntity.GetCryptoPaymentData(),
                                Invoice: entity))
                    )
                )
                .Where(tuple => tuple.PaymentMethod?.GetPaymentMethodDetails()?.Activated is true)
                .ToList();

            var existingPaymentData = expandedInvoices
                .SelectMany(tuple =>
                    tuple.ExistingPayments
                        .Where(valueTuple => valueTuple.Payment.Accounted)
                )
                .ToList();

            // All payments are not accounted, or there are no payments
            var noAccountedPaymentInvoices = expandedInvoices
                .Where(tuple =>
                    tuple.ExistingPayments
                        .All(valueTuple => !valueTuple.Payment.Accounted)
                )
                .Select(p => ProcessUnaccounted(p, network))
                .ToList();
        }
    }

    private async Task ProcessUnaccounted(
        (InvoiceEntity Invoice, PaymentMethod PaymentMethod,
            IEnumerable<(PaymentEntity Payment, BSCPaymentData PaymentData, InvoiceEntity Invoice)> ExistingPayments)
            valueTuple,
        BSCBTCPayNetwork network)
    {
        var destinationAddress = valueTuple.PaymentMethod.DepositAddress;
        Logs.PayServer.LogDebug(
            $"Checking address: {destinationAddress} for new payments on {network.CryptoCode}");
        var balance = await _web3.GetBalance(destinationAddress, network);
        
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is InvoiceEvent invoiceEvent)
        {
            if (invoiceEvent.Name == InvoiceEvent.Created)
            {
                AddInvoice(invoiceEvent.Invoice);
            }
            else if (
                invoiceEvent.Name == InvoiceEvent.MarkedCompleted ||
                invoiceEvent.Name == InvoiceEvent.MarkedInvalid ||
                invoiceEvent.Name == InvoiceEvent.PaidInFull ||

                // Al payments are confirmed. We should keep tracking this payments in a
                // very low priority watcher
                invoiceEvent.Name == InvoiceEvent.Confirmed ||

                // All payment confirmation counts are greater than what the system tracks 
                invoiceEvent.Name == InvoiceEvent.Completed ||
                invoiceEvent.Name == InvoiceEvent.PaymentSettled ||
                invoiceEvent.Name == InvoiceEvent.PaidAfterExpiration ||

                // Make sure we catch this in a lower priority watcher service
                invoiceEvent.Name == InvoiceEvent.Expired ||
                invoiceEvent.Name == InvoiceEvent.ExpiredPaidPartial ||
                invoiceEvent.Name == InvoiceEvent.FailedToConfirm // Invoice is paid, but not all payments are confirmed
            )
            {
                RemoveInvoice(invoiceEvent.InvoiceId);
            }
        }
        else if (evt is InvoiceStopWatchedEvent stopEvent)
        {
            RemoveInvoice(stopEvent.InvoiceId);
        }
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<InvoiceEvent>();
    }

    private void AddInvoice(InvoiceEntity invoice)
    {
        _invoices[invoice.Id] = invoice;
    }

    private void RemoveInvoice(string invoiceId)
    {
        _invoices.Remove(invoiceId);
    }

    public class BSCBalanceFetched : BSCEvent
    {
        public BlockParameter BlockParameter { get; set; }
        public int ChainId { get; set; }
        public string Address { get; set; }
        public string CryptoCode { get; set; }
        public BigInteger Amount { get; set; }
        public InvoiceEntity InvoiceEntity { get; set; }
        public PaymentEntity MatchedExistingPayment { get; set; }
        public BSCPaymentMethodDetails PaymentMethodDetails { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
}
