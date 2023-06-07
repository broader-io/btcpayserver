using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.ProsusPay.Exceptions;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using PayoutData = BTCPayServer.Data.PayoutData;

namespace BTCPayServer.Plugins.ProsusPay
{
    public class ProsusPayFeeWatcher : IHostedService
    {
        private readonly EventAggregator _eventAggregator;
        Task _Loop;
        CancellationTokenSource _Cts;
        private readonly StoreRepository _storeRepository;
        private readonly ProsusPayDbContextFactory _dbContextFactory;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly SettingsRepository _settingsRepository;
        private readonly ProsusPayoutService _ProsusPayoutService;
        private readonly ProsusPayDBHelper _ProsusPayDBHelper;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly IEnumerable<IPayoutHandler> _payoutHandlers;
        private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;
        public Logs _Logs { get; }

        public ProsusPayFeeWatcher(
            EventAggregator eventAggregator,
            Logs logs,
            StoreRepository storeRepository,
            ProsusPayDbContextFactory dbContextFactory,
            InvoiceRepository invoiceRepository,
            SettingsRepository settingsRepository,
            ProsusPayoutService prosusPayoutService,
            ProsusPayDBHelper prosusPayDBHelper,
            BTCPayNetworkProvider networkProvider,
            IEnumerable<IPayoutHandler> payoutHandlers,
            BTCPayNetworkJsonSerializerSettings jsonSerializerSettings
        )

        {
            _eventAggregator = eventAggregator;
            _storeRepository = storeRepository;
            _dbContextFactory = dbContextFactory;
            _invoiceRepository = invoiceRepository;
            _Logs = logs;
            _settingsRepository = settingsRepository;
            _ProsusPayoutService = prosusPayoutService;
            _ProsusPayDBHelper = prosusPayDBHelper;
            _networkProvider = networkProvider;
            _payoutHandlers = payoutHandlers;
            _jsonSerializerSettings = jsonSerializerSettings;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _Loop = StartLoop(_Cts.Token);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        /**
         * Finds all the unpaid invoice payments per store and attempts to create a fee payout per store.
         */
        private async Task StartLoop(CancellationToken cancellationToken)
        {
            _Logs.PayServer.LogInformation("Starting Prosus Pay | Invoice Fee Watcher");
            
            while (!cancellationToken.IsCancellationRequested)
            {
                await CreatePayouts(cancellationToken);

                await ConfirmProsusPayPayments(cancellationToken);

                await MarkInvoiceCompleted(cancellationToken);

                await Task.Delay(PollInterval, cancellationToken);
            }

            Console.Write(cancellationToken);
        }

        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);
        
        private async Task CreatePayouts(CancellationToken cancellationToken)
        {
            try
            {
                await using var ctx = _dbContextFactory.CreateContext();
                var pPayments = ctx.ProsusPayPayments
                    .Where(p => p.Status == ProsusPayPaymentsStatus.InvoiceCompleted).ToList();

                var pStorePayments = pPayments.GroupBy(p => p.StoreDataId);

                foreach (var paymentGroup in pStorePayments)
                {
                    var storeId = paymentGroup.Key;
                    var store = ctx.Stores.First(s => s.Id == storeId);

                    // Payments grouped by crypto code
                    var cryptoCodeGroup = paymentGroup
                        .GroupBy(pg => pg.CryptoCode);
                    foreach (var cryptoCodePayments in cryptoCodeGroup)
                    {
                        var cryptoCode = cryptoCodePayments.Key;
                        var paymentMethod = store.GetSupportedPaymentMethods(_networkProvider)
                            .First(p => p.PaymentId.CryptoCode == cryptoCode);

                        var total = cryptoCodePayments
                            .Aggregate((decimal)0.0, (acc, x) => acc + x.CryptoAmount);

                        // Create a payout only if the amount exceeds the configured minimum payout amount for this
                        // crypto coin. This is to avoid problems associated to payments with very low fees such as 
                        // minimum network fee requirements.
                        var cryptoConfig = await ProsusPayPaymentHelper
                            .GetCodeConfiguration(_settingsRepository, cryptoCode);
                        var minAmount = cryptoConfig.minPaymentAmount;
                        if (total > minAmount)
                        {
                            try
                            {
                                var payout = await _ProsusPayoutService.CreatePayout(
                                    storeId,
                                    paymentMethod.PaymentId,
                                    total,
                                    cryptoConfig.address,
                                    cancellationToken
                                );

                                foreach (var cryptoCodePayment in cryptoCodePayments)
                                {
                                    cryptoCodePayment.PayoutDataId = payout.Id;
                                    cryptoCodePayment.Status = ProsusPayPaymentsStatus.Pending;
                                    ctx.Update(cryptoCodePayment);
                                    await ctx.SaveChangesAsync();
                                }
                            }
                            catch (ProsusPayPayoutException e)
                            {
                                _Logs.PayServer.LogWarning("Found duplicate Payout");
                            }
                            catch (Exception e)
                            {
                                Console.Write(e);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.Write(e);
            }
        }

        private async Task ConfirmProsusPayPayments(CancellationToken cancellationToken)
        {
            try
            {
                await using var ctx = _dbContextFactory.CreateContext();
                var payments = ctx.Payouts.Join(ctx.ProsusPayPayments,
                        p => p.Id, pp => pp.PayoutDataId,
                        (p, pp) => new {Payout = p, ProsusPayPayment = pp})
                    .Where(p => p.Payout.State == PayoutState.Completed)
                    .Where(p => p.ProsusPayPayment.Status == ProsusPayPaymentsStatus.Pending)
                    .ToList();
                foreach (var pPayment in payments)
                {
                    var payout = pPayment.Payout;
                    var proof = JsonConvert.DeserializeObject<PayoutTransactionOnChainBlob>(
                        Encoding.UTF8.GetString(payout.Proof), 
                        _jsonSerializerSettings.GetSerializer(payout.GetPaymentMethodId().CryptoCode));
                    var ppPayment = pPayment.ProsusPayPayment;
                    ppPayment.Status = ProsusPayPaymentsStatus.Paid;
                    ppPayment.TransactionId = proof.TransactionId.ToString();
                    ctx.Update(ppPayment);
                    ctx.SaveChanges();
                }
            }
            catch (Exception e)
            {
                Console.Write(e);
            }
        }
        
        /**
         * Looks at invoices that have been completed and marks any associated ProsusPayPayments as
         * InvoiceCompleted.
         */
        private async Task MarkInvoiceCompleted(CancellationToken cancellationToken)
        {
            await using var ctx = _dbContextFactory.CreateContext();
            
            // ProsusPayPayments that are Created with associated invoices that are complete
            var iPayments = ctx.Invoices.Join(ctx.ProsusPayPayments,
                    i => i.Id, pp => pp.InvoiceDataId,
                    (i, pp) => new {Invoice = i, ProsusPayPayment = pp})
                .Where(ip => ip.ProsusPayPayment.Status == ProsusPayPaymentsStatus.Created)
                .Where(ip => ip.Invoice.Status == InvoiceStatusLegacy.Complete.ToString().ToLowerInvariant())
                .ToList();
            foreach (var ipayment in iPayments)
            {
                ipayment.ProsusPayPayment.Status = ProsusPayPaymentsStatus.InvoiceCompleted;
                ctx.Update(ipayment.ProsusPayPayment);
                await ctx.SaveChangesAsync();
            }
        }
    }
}
