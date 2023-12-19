using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.BSC.Extensions;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Nethereum.RPC.Eth.DTOs;

namespace BTCPayServer.Plugins.BSC.Services;

public class BSCPaymentService : BSCBasePollingService
{
    private InvoiceRepository _invoiceRepository;
    private PaymentService _paymentService;

    public BSCPaymentService(
        int chainId,
        TimeSpan pollInterval,
        SettingsRepository settingsRepository,
        PaymentService paymentService,
        EventAggregator eventAggregator,
        BTCPayNetworkProvider btcPayNetworkProvider,
        InvoiceRepository invoiceRepository,
        Logs logs
    ) : base(chainId, pollInterval, btcPayNetworkProvider, settingsRepository, eventAggregator, logs)
    {
        _invoiceRepository = invoiceRepository;
        _paymentService = paymentService;
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is BSCBEP20TransactionPollerService.NewBSCTransactionEvent txEvent)
        {
            var bSCPaymentMethod = new BSCSupportedPaymentMethod();
            bSCPaymentMethod.CryptoCode = txEvent.Network.CryptoCode;
            
            var invoice = await InvoiceExtensions.GetInvoiceFromAddress(
                txEvent.To, bSCPaymentMethod.PaymentId, _invoiceRepository);

            if (invoice != null)
            {
                var paymentMethod = invoice.GetPaymentMethod(txEvent.Network, BSCPaymentType.Instance);
                var paymentMethodDetails = (BSCPaymentMethodDetails)paymentMethod.GetPaymentMethodDetails();

                var paymentData = new BSCPaymentData
                {
                    Address = txEvent.To,
                    AddressFrom = txEvent.From,
                    BlockParameter = new BlockParameter(txEvent.BlockNumber),
                    ConfirmationCount = 0,
                    CryptoCode = txEvent.Network.CryptoCode,
                    KeyPath = paymentMethodDetails.KeyPath,
                    Network = txEvent.Network,
                    Value = txEvent.Value,
                    TransactionHash = txEvent.TransactionHash,
                    TransactionIndex = txEvent.TransactionIndex,
                    BlockHash = txEvent.BlockHash,
                    ContractAddress = txEvent.ContractAddress
                };

                var alreadyExist = GetAllBSCPaymentData(invoice, false)
                    .Any(c =>
                        c.GetPaymentId() == paymentData.GetPaymentId());

                if (!alreadyExist)
                {
                    var payment = await _paymentService.AddPayment(
                        invoice.Id,
                        DateTimeOffset.UtcNow,
                        paymentData,
                        txEvent.Network,
                        true);

                    if (payment != null)
                        await ReceivedPayment(invoice, txEvent.Network, payment);
                }
                else
                {
                    await UpdatePaymentStates(invoice.Id, txEvent.Network);
                }
            }
        }
    }

    protected override async Task PollingCallback(BSCBTCPayNetwork network, CancellationToken cancellationToken)
    {
        var invoices = await _invoiceRepository.GetPendingInvoices(
            skipNoPaymentInvoices: true,
            cancellationToken: cancellationToken);
        var tasks = invoices
            .Select(invoice => UpdatePaymentStates(invoice, network));
        await Task.WhenAll(tasks.ToArray());
    }

    async Task<InvoiceEntity> UpdatePaymentStates(string invoiceId, BSCBTCPayNetwork network)
    {
        var invoice = await _invoiceRepository.GetInvoice(invoiceId, false);
        if (invoice == null)
            return null;
        return await UpdatePaymentStates(invoice, network);
    }

    public static IEnumerable<BSCPaymentData> GetAllBSCPaymentData(InvoiceEntity invoice, bool accountedOnly)
    {
        return invoice.GetPayments(accountedOnly)
            .Where(p => p.GetPaymentMethodId()?.PaymentType == BSCPaymentType.Instance)
            .Select(p => (BSCPaymentData)p.GetCryptoPaymentData())
            .Where(data => data != null);
    }

    async Task<InvoiceEntity> UpdatePaymentStates(
        InvoiceEntity invoice,
        BSCBTCPayNetwork network)
    {
        List<PaymentEntity> updatedPaymentEntities = new();

        var latestBlockNumber = await _web3.GetLatestBlockNumberLong();

        foreach (var payment in invoice.GetPayments(network, false))
        {
            if (payment.GetPaymentMethodId()?.PaymentType != BSCPaymentType.Instance)
                continue;

            var paymentData = (BSCPaymentData)payment.GetCryptoPaymentData();

            var tx = await _web3.GetTransaction(paymentData.TransactionHash);

            // When transaction is unconfirmed, its block number is null.
            // In this case we return 0 as number of confirmations
            var confirmationCount = tx.BlockNumber == null ? 0 : latestBlockNumber - (long)tx.BlockNumber.Value;

            bool updated = false;
            if (paymentData.ConfirmationCount != confirmationCount)
            {
                if (network.MaxTrackedConfirmation >= paymentData.ConfirmationCount)
                {
                    paymentData.ConfirmationCount = confirmationCount;
                    payment.SetCryptoPaymentData(paymentData);
                    updated = true;
                }
            }

            // if needed add invoice back to pending to track number of confirmations
            if (paymentData.ConfirmationCount < network.MaxTrackedConfirmation)
                await _invoiceRepository.AddPendingInvoiceIfNotPresent(invoice.Id);

            if (updated)
                updatedPaymentEntities.Add(payment);
        }

        await _paymentService.UpdatePayments(updatedPaymentEntities);
        if (updatedPaymentEntities.Count != 0)
            EventAggregator.Publish(new InvoiceNeedUpdateEvent(invoice.Id));
        return invoice;
    }

    private async Task ReceivedPayment(
        InvoiceEntity invoice,
        BSCBTCPayNetwork network,
        PaymentEntity payment)
    {
        invoice = await UpdatePaymentStates(invoice.Id, network);
        if (invoice != null)
            EventAggregator.Publish(
                new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) {Payment = payment});
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<BSCBEP20TransactionPollerService.NewBSCTransactionEvent>();
    }
}
