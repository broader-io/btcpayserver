#if ALTCOINS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.StandardTokenEIP20.ContractDefinition;

namespace BTCPayServer.Plugins.BSC.Services
{
    public class BSCInvoiceWatcher : BSCBaseHostedService
    {
        //LimitedConcurrencyLevelTaskScheduler lcts = new LimitedConcurrencyLevelTaskScheduler(50);

        private readonly InvoiceRepository _invoiceRepository;
        private readonly PaymentService _paymentService;
        private readonly HashSet<PaymentMethodId> _paymentMethods;

        private CancellationTokenSource _Cts;

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            Logs.PayServer.LogDebug($"Starting BSCWatcher for chain {_chainId}");

            await base.StartAsync(cancellationToken);
            await StartLoop(cancellationToken);

            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = StartLoop(_Cts.Token);
            //return Task.CompletedTask;
        }

        private async Task StartLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                EventAggregator.Publish(new CatchUp());
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<BSCService.ReserveBSCAddressResponse>();
            Subscribe<BSCAddressBalanceFetched>();
            Subscribe<CatchUp>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (evt is BSCAddressBalanceFetched response)
            {
                //Thread.Sleep(1000);

                if (response.ChainId != _chainId)
                {
                    return;
                }

                var network = _networks.SingleOrDefault(payNetwork =>
                    payNetwork.CryptoCode.Equals(response.CryptoCode, StringComparison.InvariantCultureIgnoreCase));

                if (network is null)
                {
                    return;
                }

                var invoice = response.InvoiceEntity;
                if (invoice is null)
                {
                    return;
                }

                var existingPayment = response.MatchedExistingPayment;

                if (existingPayment is null && response.Amount > 0)
                {
                    //new payment
                    var paymentData = new BSCPaymentData()
                    {
                        Address = response.Address,
                        CryptoCode = response.CryptoCode,
                        Value = response.Amount,
                        Network = network,
                        BlockParameter = response.BlockParameter,
                        ConfirmationCount = 0,
                        KeyPath = response.PaymentMethodDetails.KeyPath
                    };
                    var payment = await _paymentService.AddPayment(invoice.Id, DateTimeOffset.UtcNow,
                        paymentData, network, true);
                    if (payment != null) ReceivedPayment(invoice, payment);
                }
                else if (existingPayment != null)
                {
                    var cd = (BSCPaymentData)existingPayment.GetCryptoPaymentData();
                    //existing payment amount was changed. Set to unaccounted and register as a new payment.
                    if (response.Amount == 0 || response.Amount != cd.Value)
                    {
                        existingPayment.Accounted = false;

                        await _paymentService.UpdatePayments(new List<PaymentEntity>() {existingPayment});
                        if (response.Amount > 0)
                        {
                            var paymentData = new BSCPaymentData()
                            {
                                Address = response.Address,
                                CryptoCode = response.CryptoCode,
                                Value = response.Amount,
                                Network = network,
                                BlockParameter = response.BlockParameter,
                                ConfirmationCount =
                                    response.BlockParameter.ParameterType ==
                                    BlockParameter.BlockParameterType.blockNumber
                                        ? 1
                                        : 0,
                                KeyPath = cd.KeyPath
                            };
                            var payment = await _paymentService.AddPayment(invoice.Id, DateTimeOffset.UtcNow,
                                paymentData, network, true);
                            if (payment != null) ReceivedPayment(invoice, payment);
                        }
                    }
                    else if (response.Amount == cd.Value)
                    {
                        //transition from pending to 1 confirmed
                        if (
                            cd.BlockParameter.ParameterType == BlockParameter.BlockParameterType.pending 
                            && response.BlockParameter.ParameterType ==
                            BlockParameter.BlockParameterType.blockNumber)
                        {
                            cd.ConfirmationCount = 1;
                            cd.BlockParameter = response.BlockParameter;

                            existingPayment.SetCryptoPaymentData(cd);
                            await _paymentService.UpdatePayments(new List<PaymentEntity>() {existingPayment});

                            EventAggregator.Publish(new InvoiceNeedUpdateEvent(invoice.Id));
                        }
                        //increment confirm count
                        else if (response.BlockParameter.ParameterType ==
                                 BlockParameter.BlockParameterType.blockNumber)
                        {
                            // if (response.BlockParameter.BlockNumber.Value > cd.BlockParameter)
                            // {
                            //     cd.ConfirmationCount =
                            //         (long)(response.BlockParameter.BlockNumber.Value - cd.BlockParameter);
                            // }
                            // else
                            // {
                            //     cd.BlockParameter = (long)response.BlockParameter.BlockNumber.Value;
                            //     cd.ConfirmationCount = 1;
                            // }

                            existingPayment.SetCryptoPaymentData(cd);
                            await _paymentService.UpdatePayments(new List<PaymentEntity>() {existingPayment});

                            EventAggregator.Publish(new InvoiceNeedUpdateEvent(invoice.Id));
                        }
                    }
                }
            }

            if (evt is CatchUp)
            {
                //DateTimeOffset start = DateTimeOffset.Now;

                await Task.Run(async () =>
                {
                    //while (true)
                    {
                        try
                        {
                            await UpdateAnyPendingBSCPaymentAndAddressWatchList(cancellationToken);
                            //TimeSpan diff = DateTimeOffset.Now - start;
                            Thread.Sleep(5000);
                            //_ = await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ContinueWith(task =>
                            //{
                            Logs.PayServer.LogDebug("Running CatchUp");
                            EventAggregator.Publish(new CatchUp());
                            //return Task.CompletedTask;
                            //}, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);
                        }
                        catch (Exception e)
                        {
                            Logs.PayServer.LogError(e, "Error in CatchUp");
                        }
                    }
                });
            }
        }

        class CatchUp
        {
            public override string ToString()
            {
                return "";
            }
        }

        // public override Task StopAsync(CancellationToken cancellationToken)
        // {
        //     Logs.PayServer.LogDebug($"Stopping BSCWatcher for chain {ChainId}");
        //     return null;
        // }


        private async Task UpdateAnyPendingBSCPaymentAndAddressWatchList(CancellationToken cancellationToken)
        {
            var invoiceIds = await _invoiceRepository.GetPendingInvoices();
            if (!invoiceIds.Any())
            {
                return;
            }

            var strInvoiceIds = invoiceIds.Select(i => i.Id).ToArray();
            var invoices = await _invoiceRepository.GetInvoices(new InvoiceQuery() {InvoiceId = strInvoiceIds});
            invoices = invoices
                .Where(entity =>
                    _paymentMethods.Any(id => entity.GetPaymentMethod(id)?.GetPaymentMethodDetails()?.Activated is true))
                .ToArray();

            await UpdatePaymentStates(invoices, cancellationToken);
        }

        private BlockParameter? LastBlock = null;

        private async Task UpdatePaymentStates(InvoiceEntity[] invoices, CancellationToken cancellationToken)
        {
            if (!invoices.Any())
            {
                return;
            }

            var currentBlock = await _web3.GetLatestBlockNumber();

            foreach (var network in _networks)
            {
                var paymentMethodId = new PaymentMethodId(network.CryptoCode, BSCPaymentType.Instance);
                var expandedInvoices = invoices
                    .Select(entity => (
                        Invoice: entity,
                        PaymentMethodDetails: entity.GetPaymentMethods().TryGet(paymentMethodId),
                        ExistingPayments: entity.GetPayments(network, true).Select(paymentEntity => (
                            Payment: paymentEntity,
                            PaymentData: (BSCPaymentData)paymentEntity.GetCryptoPaymentData(),
                            Invoice: entity))
                    )).Where(tuple => tuple.PaymentMethodDetails?.GetPaymentMethodDetails()?.Activated is true)
                    .ToList();

                var existingPaymentData = expandedInvoices.SelectMany(tuple =>
                    tuple.ExistingPayments.Where(valueTuple => valueTuple.Payment.Accounted)).ToList();

                var noAccountedPaymentInvoices = expandedInvoices.Where(tuple =>
                    tuple.ExistingPayments.All(valueTuple => !valueTuple.Payment.Accounted)).ToList();

                var tasks = new List<Task>();
                if (existingPaymentData.Any() && currentBlock != LastBlock)
                {
                    Logs.PayServer.LogDebug(
                        $"Checking {existingPaymentData.Count} existing payments on {expandedInvoices.Count} invoices on {network.CryptoCode}");

                    tasks.Add(Task.WhenAll(existingPaymentData.Select(async tuple =>
                    {
                        var bal = await _web3.GetBalance(tuple.PaymentData.Address, network);

                        EventAggregator.Publish(new BSCAddressBalanceFetched()
                        {
                            Address = tuple.PaymentData.Address,
                            CryptoCode = network.CryptoCode,
                            Amount = bal,
                            MatchedExistingPayment = tuple.Payment,
                            BlockParameter = currentBlock,
                            ChainId = _chainId,
                            InvoiceEntity = tuple.Invoice,
                        });
                    })).ContinueWith(task =>
                    {
                        LastBlock = currentBlock;
                    }, TaskScheduler.Current));
                }

                if (noAccountedPaymentInvoices.Any())
                {
                    Logs.PayServer.LogDebug(
                        $"Checking {noAccountedPaymentInvoices.Count} addresses for new payments on {network.CryptoCode}");
                    var blockParameter = BlockParameter.CreatePending();
                    tasks.AddRange(noAccountedPaymentInvoices.Select(async tuple =>
                    {
                        var address = tuple.PaymentMethodDetails.DepositAddress;
                        var bal = await _web3.GetBalance(address, network);
                        EventAggregator.Publish(new BSCAddressBalanceFetched()
                        {
                            Address = address,
                            CryptoCode = network.CryptoCode,
                            Amount = bal,
                            MatchedExistingPayment = null,
                            BlockParameter = blockParameter,
                            ChainId = _chainId,
                            InvoiceEntity = tuple.Invoice,
                            PaymentMethodDetails =
                                (BSCPaymentMethodDetails)tuple.PaymentMethodDetails.GetPaymentMethodDetails()
                        });
                    }));
                }

                await Task.WhenAll(tasks);
            }
        }

        public class BSCAddressBalanceFetched
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

        private void ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
        {
            EventAggregator.Publish(
                new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) {Payment = payment});
        }

        public BSCInvoiceWatcher(
            int chainId,
            BTCPayNetworkProvider btcPayNetworkProvider,
            SettingsRepository settingsRepository,
            EventAggregator eventAggregator,
            InvoiceRepository invoiceRepository,
            PaymentService paymentService,
            Logs logs) :
            base(
                chainId,
                eventAggregator,
                settingsRepository,
                btcPayNetworkProvider,
                logs)
        {
            _invoiceRepository = invoiceRepository;
            _paymentService = paymentService;

            _paymentMethods = _networks
                .Select(network => new PaymentMethodId(network.CryptoCode, BSCPaymentType.Instance))
                .ToHashSet();
        }
    }
}
#endif
