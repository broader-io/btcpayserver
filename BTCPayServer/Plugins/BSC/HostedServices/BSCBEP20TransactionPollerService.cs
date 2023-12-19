using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;

namespace BTCPayServer.Plugins.BSC.Services;

public class BSCBEP20TransactionPollerService : BSCBasePollingService
{
    private HashSet<BlockParameter> _pendingBlocks = new();
    private readonly HashSet<string> _addresses = new();
    private readonly Dictionary<string, InvoiceEntity> _invoices = new();
    private InvoiceRepository _invoiceRepository;

    public BSCBEP20TransactionPollerService(
        int chainId,
        TimeSpan pollInterval,
        BTCPayNetworkProvider btcPayNetworkProvider,
        InvoiceRepository invoiceRepository,
        SettingsRepository settingsRepository,
        EventAggregator eventAggregator, Logs logs) :
        base(chainId, pollInterval, btcPayNetworkProvider, settingsRepository, eventAggregator, logs)
    {
        _invoiceRepository = invoiceRepository;
    }

    protected override Task PollingCallback(BSCBTCPayNetwork network, CancellationToken cancellationToken)
    {
        if (network is BEP20BTCPayNetwork bep20Network)
        {
            EventAggregator.Publish(new BSCBEP20TransactionPollerHeartBeat {network = bep20Network});
        }

        return Task.CompletedTask;
    }

    private async Task ProcessTransactionHeartBeat(BEP20BTCPayNetwork network)
    {
        Console.WriteLine($"Heartbeat, addresses {_addresses.Count}, pending blocks: {_pendingBlocks.Count}");
        try
        {
            if (_addresses.Count == 0)
            {
                _pendingBlocks = new();
            }

            if (_pendingBlocks.Count > 0 && _addresses.Count > 0)
            {
                var transferEventHandler = _web3.GetTransferEventDTOEvent(network.SmartContractAddress);

                var copy = _pendingBlocks.ToArray();
                long initialBlock = (long)copy.Min(b => b.BlockNumber.Value);
                long endBlock = (long)copy.Max(b => b.BlockNumber.Value);

                Console.WriteLine($"Processing block range: {initialBlock}-{endBlock}");

                Logs.PayServer.LogDebug($"Processing transactions from block: {initialBlock} to {endBlock}");

                var rangeSize = Math.Max(1, Math.Min(
                    _settings.TransactionPollerRange,
                    endBlock - initialBlock));
                for (long i = endBlock; i >= initialBlock; i -= rangeSize)
                {
                    var filterInput = transferEventHandler.CreateFilterInput(
                        null,
                        _addresses.ToArray(),
                        new BlockParameter(new HexBigInteger(new BigInteger(i - rangeSize))),
                        new BlockParameter(new HexBigInteger(i)));

                    var allTransferEventsForContract =
                        await transferEventHandler.GetAllChangesAsync(filterInput);
                    if (allTransferEventsForContract != null && allTransferEventsForContract.Count > 0)
                    {
                        Logs.PayServer.LogDebug($"Received {allTransferEventsForContract.Count} transactions");

                        allTransferEventsForContract.ForEach(e =>
                        {
                            var evt = e.Event;
                            var log = e.Log;

                            Console.WriteLine($"Received payment for {evt.To}");
                            Console.WriteLine($"BlockNumber: {log.BlockNumber.Value}");
                            Console.WriteLine($"TX: {log.TransactionHash}");

                            EventAggregator.Publish(new NewBSCTransactionEvent
                            {
                                Network = network,
                                From = evt.From,
                                To = evt.To,
                                Value = evt.Value,
                                ContractAddress = log.Address,
                                BlockHash = log.BlockHash,
                                BlockNumber = log.BlockNumber,
                                LogIndex = log.LogIndex,
                                Topics = log.Topics,
                                TransactionHash = log.TransactionHash,
                                TransactionIndex = log.TransactionIndex
                            });
                        });
                    }

                    await _web3.UpdateLastSeenBlockNumberSettings(i);

                    await Task.Delay(500);
                }

                await _web3.UpdateLastSeenBlockNumberSettings(endBlock);
                _pendingBlocks = new();
            }
        }

        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    protected override async Task StartPoller(CancellationToken cancellationToken)
    {
        var invoices = await _invoiceRepository
            .GetPendingInvoices(cancellationToken: cancellationToken);
        if (invoices.Length > 0)
        {
            invoices.ToList().ForEach(AddInvoice);
            UpdateInvoiceAddresses();
        }
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is BSCBlockPoller.NewBlockEvent newBlockEvent && newBlockEvent.chainId == _chainId)
        {
            _pendingBlocks.Add(newBlockEvent.blockParameter);
        }
        else if (evt is BSCBEP20TransactionPollerHeartBeat heartBeatEvent)
        {
            if (heartBeatEvent.network is BEP20BTCPayNetwork bep20Network)
            {
                await ProcessTransactionHeartBeat(bep20Network);
            }
        }
        else if (evt is InvoiceEvent invoiceEvent)
        {
            ProcessInvoiceEvent(invoiceEvent);
        }
    }

    private void ProcessInvoiceEvent(IHasInvoiceId evt)
    {
        if (evt is InvoiceEvent invoiceEvent)
        {
            if (
                invoiceEvent.Name == InvoiceEvent.Created ||
                invoiceEvent.Name == InvoiceEvent.Confirmed ||
                // All payment confirmation counts are greater than what the system tracks 
                invoiceEvent.Name == InvoiceEvent.Completed ||
                invoiceEvent.Name == InvoiceEvent.PaymentSettled ||
                invoiceEvent.Name == InvoiceEvent.PaidAfterExpiration ||
                invoiceEvent.Name == InvoiceEvent.Expired ||
                invoiceEvent.Name == InvoiceEvent.ExpiredPaidPartial ||
                invoiceEvent.Name == InvoiceEvent.FailedToConfirm // Invoice is paid, but not all payments are confirmed
            )
            {
                AddInvoice(invoiceEvent.Invoice);
                UpdateInvoiceAddresses();
            }
            else if (
                invoiceEvent.Name == InvoiceEvent.MarkedCompleted ||
                invoiceEvent.Name == InvoiceEvent.MarkedInvalid ||
                invoiceEvent.Name == InvoiceEvent.PaidInFull
            )
            {
                RemoveInvoice(invoiceEvent.InvoiceId);
                UpdateInvoiceAddresses();
            }
        }
        else if (evt is InvoiceStopWatchedEvent invoiceStopWatchEvent)
        {
            RemoveInvoice(invoiceStopWatchEvent.InvoiceId);
            UpdateInvoiceAddresses();
        }
    }

    private void UpdateInvoiceAddresses()
    {
        _invoices
            .Values
            .ToList()
            .ForEach(invoice =>
            {
                invoice
                    .GetPaymentMethods()
                    .Where(p => p.GetId().PaymentType == BSCPaymentType.Instance)
                    .Select(p => p.GetPaymentMethodDetails().GetPaymentDestination())
                    .ToList()
                    .ForEach(address => _addresses.Add(address));
            });
    }

    private void AddInvoice(InvoiceEntity invoice)
    {
        _invoices[invoice.Id] = invoice;
    }

    private void RemoveInvoice(string invoiceId)
    {
        _invoices.Remove(invoiceId);
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<BSCBlockPoller.NewBlockEvent>();
        Subscribe<BSCBEP20TransactionPollerHeartBeat>();
        Subscribe<InvoiceEvent>();
    }

    private class BSCBEP20TransactionPollerHeartBeat
    {
        public BSCBTCPayNetwork network;
    }

/*
 * [0] = EventLog<Web3Wrapper.TransferEventDTO>
Event = Web3Wrapper.TransferEventDTO
From = {string} "0x6b3B0b9dF99a9dDa59e65f2130DFfe3D77AEc7a4"
To = {string} "0xD7931924662D8086160B87D17622b23dFf04D129"
Value = {BigInteger} 19998830189345541

Log = FilterLog
Address = {string} "0x47034b3c18f17dd89ce1d7f87b9a90235158e4cc"
BlockHash = {string} "0x627e382917241e9efbace000403012b419c7aa024a2e35198ef7bfce58d526c6"
BlockNumber = {HexBigInteger} 36011645
Data = {string} "0x00000000000000000000000000000000000000000000000000470cd4815bfb05"
LogIndex = {HexBigInteger} 0
Removed = {bool} false
Topics = {object[]} object[3]
[0] = {string} "0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef"
[1] = {string} "0x0000000000000000000000006b3b0b9df99a9dda59e65f2130dffe3d77aec7a4"
[2] = {string} "0x000000000000000000000000d7931924662d8086160b87d17622b23dff04d129"
TransactionHash = {string} "0x731a795d71585dfd233001bcdfe18cd37c4008b84837cf106a8fea158aac578d"
TransactionIndex = {HexBigInteger} 0
HexValue = {string} "0x0"
Value = {BigInteger} 0
convertor = HexBigIntegerBigEndianConvertor
hexValue = {string} "0x0"
lockingObject = object
needsInitialisingValue = {bool} false
value = {BigInteger} 0
Type = {string} null
 */
    public class NewBSCTransactionEvent
    {
        public BSCBTCPayNetwork Network;
        public string From;
        public string To;
        public BigInteger Value;
        public string ContractAddress;
        public string BlockHash;
        public HexBigInteger BlockNumber;
        public HexBigInteger LogIndex;
        public bool Removed;
        public object[] Topics;
        public string TransactionHash;
        public HexBigInteger TransactionIndex;
    }
}
