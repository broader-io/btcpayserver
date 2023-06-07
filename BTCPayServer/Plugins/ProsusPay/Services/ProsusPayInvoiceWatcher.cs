using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Plugins.ProsusPay
{
    public class ProsusPayInvoiceWatcher : EventHostedServiceBase
    {
        private ProsusPayDBHelper _ProsusPayDBHelper;
        private readonly BTCPayWalletProvider _walletProvider;
        private readonly SettingsRepository _settingsRepository;
        private readonly ProsusPayDbContextFactory _dbContextFactory;

        public ProsusPayInvoiceWatcher(
            EventAggregator eventAggregator,
            Logs logs,
            ProsusPayDBHelper prosusPayDBHelper,
            BTCPayWalletProvider walletProvider,
            SettingsRepository settingsRepository,
            ProsusPayDbContextFactory contextFactory
        ) : base(eventAggregator, logs)
        {
            _ProsusPayDBHelper = prosusPayDBHelper;
            _walletProvider = walletProvider;
            _settingsRepository = settingsRepository;
            _dbContextFactory = contextFactory;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await base.StartAsync(cancellationToken);
            Logs.PayServer.LogInformation($"Starting Prosus Pay Invoice Watcher");
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<InvoiceEvent>();
            base.SubscribeToEvents();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is InvoiceEvent invoiceEvent)
            {
                var settings =
                    await _settingsRepository.GetSettingAsync<ProsusPayConfiguration>(
                        ProsusPayConfiguration.SettingsKey());

                Logs.PayServer.LogDebug($"Processing invoice event");

                if (invoiceEvent.EventCode == InvoiceEventCode.ReceivedPayment)
                {
                    handleReceivedPayment(invoiceEvent);
                }
                /*
                 * Creates an initial entry of ProsusPayPaymentsData.
                 */
                else if (invoiceEvent.EventCode == InvoiceEventCode.PaymentSettled)
                {
                    handleReceivedSettled(invoiceEvent);
                }
                else if (invoiceEvent.EventCode == InvoiceEventCode.PaidAfterExpiration) { }
            }
        }

        private async void handleReceivedPayment(InvoiceEvent invoiceEvent)
        {
            var payment = invoiceEvent.Payment;

            //We use the invoice to get the total due, in order to make a ProsusPayPayment
            //that doesn't exceed the invoice total.
            await using var ctx = _dbContextFactory.CreateContext();
            var subtotalDueGroup = invoiceEvent.Invoice.GetPaymentMethods()
                .ToDictionary(pm => pm.GetId().CryptoCode, pm => pm.Calculate());

            var network = payment.Network as BTCPayNetwork;
            var txId = ((BitcoinLikePaymentData)payment.GetCryptoPaymentData()).Outpoint.Hash;
            var wallet = _walletProvider.GetWallet(network.CryptoCode);
            var transactionResult = wallet.GetTransactionAsync(txId);
            var cryptoCode = payment.GetCryptoCode();

            var vendorFeeRate = ProsusPayPaymentHelper.GetCodeConfiguration(_settingsRepository, cryptoCode)
                .Result.feeRate;
            var vendorNetworkFee = ProsusPayPaymentHelper.GetCodeConfiguration(_settingsRepository, cryptoCode)
                .Result.networkFee;
            var sourceAddress =
                ProsusPayPaymentHelper.GetInputAddress(transactionResult.Result.Transaction, network);
            var destinationAddress =
                ((BitcoinLikePaymentData)payment.GetCryptoPaymentData()).Address.ToString();

            var originalValue = payment.GetCryptoPaymentData().GetValue();
            var originalFee = payment.NetworkFee;

            // We need to make sure we are not generating a payment that exceeds the invoice
            // amount when considering the other customer payments that could've been made.
            var vendorAmount = ProsusPayPaymentHelper.GetVendorPaymentAmount(
                cryptoCode, invoiceEvent.Invoice, invoiceEvent.Payment, _settingsRepository);

            if (vendorAmount > 0)
            {
                var newProsusPayPayment = new ProsusPayPaymentsData()
                {
                    Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(20)),
                    Created = DateTimeOffset.UtcNow,
                    CryptoCode = cryptoCode,
                    CryptoAmount = vendorAmount,
                    FeeRate = vendorFeeRate,
                    NetworkFee = vendorNetworkFee,
                    DestinationAddress = destinationAddress,
                    SourceAddress = sourceAddress.ToString(),
                    OriginalPaymentAmount = originalValue,
                    OriginalNetworkPaymentFeeAmount = originalFee,
                    OriginalTransactionId = txId.ToString(),
                    Status = ProsusPayPaymentsStatus.Created,
                    InvoiceDataId = invoiceEvent.InvoiceId,
                    StoreDataId = invoiceEvent.Invoice.StoreId,
                    OriginalPaymentDataId = payment.GetCryptoPaymentData().GetPaymentId()
                };
                ctx.Add(newProsusPayPayment);
                try
                {
                    await ctx.SaveChangesAsync();
                }
                catch (DbUpdateException e)
                {
                    Console.Write(e.Message);
                }
            }
        }

        private async void handleReceivedSettled(InvoiceEvent invoiceEvent)
        {
            // await using var ctx = _dbContextFactory.CreateContext();
            // var pPayment = ctx.ProsusPayPayments
            //     .Where(p => p.OriginalPaymentDataId == invoiceEvent.Payment.GetCryptoPaymentData().GetPaymentId())
            //     .First(p => p.Status == ProsusPayPaymentsStatus.Created);
            //
            // pPayment.Status = ProsusPayPaymentsStatus.InvoiceCompleted;
            // ctx.Update(pPayment);
            // await ctx.SaveChangesAsync();
        }
    }
}
