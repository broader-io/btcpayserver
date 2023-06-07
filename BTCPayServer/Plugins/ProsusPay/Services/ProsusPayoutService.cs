using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.ProsusPay.Exceptions;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using PayoutData = BTCPayServer.Data.PayoutData;

namespace BTCPayServer.Plugins.ProsusPay;

public class ProsusPayoutService
{
    private readonly IEnumerable<IPayoutHandler> _payoutHandlers;
    private readonly ProsusPayDbContextFactory _dbContextFactory;
    private readonly CurrencyNameTable _currencyNameTable;
    private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly RateFetcher _rateFetcher;

    public ProsusPayoutService(
        ProsusPayDbContextFactory dbContextFactory,
        CurrencyNameTable currencyNameTable,
        IEnumerable<IPayoutHandler> payoutHandlers,
        BTCPayNetworkJsonSerializerSettings jsonSerializerSettings,
        BTCPayNetworkProvider networkProvider,
        RateFetcher rateFetcher
    )
    {
        _dbContextFactory = dbContextFactory;
        _currencyNameTable = currencyNameTable;
        _payoutHandlers = payoutHandlers;
        _jsonSerializerSettings = jsonSerializerSettings;
        _networkProvider = networkProvider;
        _rateFetcher = rateFetcher;
    }

    public async Task<PayoutData> CreatePayout(string storeId, PaymentMethodId paymentMethodId,
        decimal amount, string address, CancellationToken cancellationToken)
    {
        await using var ctx = _dbContextFactory.CreateContext();
        var payoutHandler =
            _payoutHandlers.FindPayoutHandler(paymentMethodId);
        var resDestination = await payoutHandler
            .ParseClaimDestination(paymentMethodId, address, cancellationToken);

        var claimRequest = new ClaimRequest()
        {
            Destination = resDestination.destination,
            PullPaymentId = null,
            Value = amount,
            PaymentMethodId = paymentMethodId,
            StoreId = storeId,
            PreApprove = true
        };

        cancellationToken.ThrowIfCancellationRequested();
        var cts = new TaskCompletionSource<ClaimRequest.ClaimResponse>(TaskCreationOptions
            .RunContinuationsAsynchronously);
        await CreatePayout(new ProsusPayPayoutRequest(cts, claimRequest));
        var result = await cts.Task;

        switch (result.Result)
        {
            case ClaimRequest.ClaimResult.Ok:
                return result.PayoutData;
            case ClaimRequest.ClaimResult.Duplicate:
                throw new ProsusPayPayoutException(ProsusPayPayoutException.CreatePayoutResult.Duplicate);
            case ClaimRequest.ClaimResult.AmountTooLow:
                throw new ProsusPayPayoutException(ProsusPayPayoutException.CreatePayoutResult.AmountTooLow);
            case ClaimRequest.ClaimResult.PaymentMethodNotSupported:
                throw new ProsusPayPayoutException(
                    ProsusPayPayoutException.CreatePayoutResult.PaymentMethodNotSupported);
            default:
                throw new NotSupportedException("Unsupported ClaimResult");
        }
    }


    private async Task CreatePayout(ProsusPayPayoutRequest req)
    {
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await using var ctx = _dbContextFactory.CreateContext();
            var withoutPullPayment = req.ClaimRequest.PullPaymentId is null;
            var pp = string.IsNullOrEmpty(req.ClaimRequest.PullPaymentId)
                ? null
                : await ctx.PullPayments.FindAsync(req.ClaimRequest.PullPaymentId);

            if (!withoutPullPayment && (pp is null || pp.Archived))
            {
                req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Archived));
                return;
            }

            PullPaymentBlob ppBlob = null;
            if (!withoutPullPayment)
            {
                if (pp.IsExpired(now))
                {
                    req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Expired));
                    return;
                }

                if (!pp.HasStarted(now))
                {
                    req.Completion.TrySetResult(
                        new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.NotStarted));
                    return;
                }

                ppBlob = pp.GetBlob();

                if (!ppBlob.SupportedPaymentMethods.Contains(req.ClaimRequest.PaymentMethodId))
                {
                    req.Completion.TrySetResult(
                        new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.PaymentMethodNotSupported));
                    return;
                }
            }

            var payoutHandler =
                _payoutHandlers.FindPayoutHandler(req.ClaimRequest.PaymentMethodId);
            if (payoutHandler is null)
            {
                req.Completion.TrySetResult(
                    new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.PaymentMethodNotSupported));
                return;
            }

            if (req.ClaimRequest.Destination.Id != null)
            {
                if (duplicatesExist(req.ClaimRequest.Value.Value, req.ClaimRequest.Destination.Id, ctx))
                {
                    req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Duplicate));
                    return;
                }
            }

            if (req.ClaimRequest.Value <
                await payoutHandler.GetMinimumPayoutAmount(req.ClaimRequest.PaymentMethodId,
                    req.ClaimRequest.Destination))
            {
                req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.AmountTooLow));
                return;
            }

            var payoutsRaw = withoutPullPayment
                ? null
                : await ctx.Payouts.GetPayoutInPeriod(pp, now)
                    .Where(p => p.State != PayoutState.Cancelled).ToListAsync();

            var payouts = payoutsRaw?.Select(o => new {Entity = o, Blob = o.GetBlob(_jsonSerializerSettings)});
            var limit = ppBlob?.Limit ?? 0;
            var totalPayout = payouts?.Select(p => p.Blob.Amount)?.Sum();
            var claimed = req.ClaimRequest.Value is decimal v ? v : limit - (totalPayout ?? 0);
            if (totalPayout is not null && totalPayout + claimed > limit)
            {
                req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Overdraft));
                return;
            }

            if (!withoutPullPayment && (claimed < ppBlob.MinimumClaim || claimed == 0.0m))
            {
                req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.AmountTooLow));
                return;
            }

            var payout = new PayoutData()
            {
                Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(20)),
                Date = now,
                State = PayoutState.AwaitingApproval,
                PullPaymentDataId = req.ClaimRequest.PullPaymentId,
                PaymentMethodId = req.ClaimRequest.PaymentMethodId.ToString(),
                Destination = req.ClaimRequest.Destination.Id,
                StoreDataId = req.ClaimRequest.StoreId ?? pp?.StoreId
            };
            var payoutBlob = new PayoutBlob() {Amount = claimed, Destination = req.ClaimRequest.Destination.ToString()};
            payout.SetBlob(payoutBlob, _jsonSerializerSettings);
            await ctx.Payouts.AddAsync(payout);
            try
            {
                await payoutHandler.TrackClaim(req.ClaimRequest.PaymentMethodId, req.ClaimRequest.Destination);
                await ctx.SaveChangesAsync();
                if (req.ClaimRequest.PreApprove.GetValueOrDefault(ppBlob?.AutoApproveClaims is true))
                {
                    payout.StoreData = await ctx.Stores.FindAsync(payout.StoreDataId);
                    var rateResult = await GetRate(payout, null, CancellationToken.None);
                    if (rateResult.BidAsk != null)
                    {
                        var approveResult = new TaskCompletionSource<PullPaymentHostedService.PayoutApproval.Result>();
                        await HandleApproval(new PullPaymentHostedService.PayoutApproval()
                        {
                            PayoutId = payout.Id,
                            Revision = payoutBlob.Revision,
                            Rate = rateResult.BidAsk.Ask,
                            Completion = approveResult
                        });

                        if ((await approveResult.Task) == PullPaymentHostedService.PayoutApproval.Result.Ok)
                        {
                            payout.State = PayoutState.AwaitingPayment;
                        }
                    }
                }

                req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Ok, payout));
                // await _notificationSender.SendNotification(new StoreScope(payout.StoreDataId),
                //     new PayoutNotification()
                //     {
                //         StoreId = payout.StoreDataId,
                //         Currency = ppBlob?.Currency ?? req.ClaimRequest.PaymentMethodId.CryptoCode,
                //         Status = payout.State,
                //         PaymentMethod = payout.PaymentMethodId,
                //         PayoutId = payout.Id
                //     });
            }
            catch (DbUpdateException)
            {
                req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Duplicate));
            }
        }
        catch (Exception ex)
        {
            req.Completion.TrySetException(ex);
        }
    }

    public Task<RateResult> GetRate(PayoutData payout, string explicitRateRule, CancellationToken cancellationToken)
    {
        var ppBlob = payout.PullPaymentData?.GetBlob();
        var payoutPaymentMethod = payout.GetPaymentMethodId();
        var currencyPair = new CurrencyPair(payoutPaymentMethod.CryptoCode,
            ppBlob?.Currency ?? payoutPaymentMethod.CryptoCode);
        RateRule rule = null;
        try
        {
            if (explicitRateRule is null)
            {
                var storeBlob = payout.StoreData.GetStoreBlob();
                var rules = storeBlob.GetRateRules(_networkProvider);
                rules.Spread = 0.0m;
                rule = rules.GetRuleFor(currencyPair);
            }
            else
            {
                rule = RateRule.CreateFromExpression(explicitRateRule, currencyPair);
            }
        }
        catch (Exception)
        {
            throw new FormatException("Invalid RateRule");
        }

        return _rateFetcher.FetchRate(rule, cancellationToken);
    }

    private async Task HandleApproval(PullPaymentHostedService.PayoutApproval req)
    {
        try
        {
            using var ctx = _dbContextFactory.CreateContext();
            var payout = await ctx.Payouts.Include(p => p.PullPaymentData).Where(p => p.Id == req.PayoutId)
                .FirstOrDefaultAsync();
            if (payout is null)
            {
                req.Completion.SetResult(PullPaymentHostedService.PayoutApproval.Result.NotFound);
                return;
            }

            if (payout.State != PayoutState.AwaitingApproval)
            {
                req.Completion.SetResult(PullPaymentHostedService.PayoutApproval.Result.InvalidState);
                return;
            }

            var payoutBlob = payout.GetBlob(this._jsonSerializerSettings);
            if (payoutBlob.Revision != req.Revision)
            {
                req.Completion.SetResult(PullPaymentHostedService.PayoutApproval.Result.OldRevision);
                return;
            }

            if (!PaymentMethodId.TryParse(payout.PaymentMethodId, out var paymentMethod))
            {
                req.Completion.SetResult(PullPaymentHostedService.PayoutApproval.Result.NotFound);
                return;
            }

            payout.State = PayoutState.AwaitingPayment;

            if (payout.PullPaymentData is null ||
                paymentMethod.CryptoCode == payout.PullPaymentData.GetBlob().Currency)
                req.Rate = 1.0m;
            var cryptoAmount = payoutBlob.Amount / req.Rate;
            var payoutHandler = _payoutHandlers.FindPayoutHandler(paymentMethod);
            if (payoutHandler is null)
                throw new InvalidOperationException($"No payout handler for {paymentMethod}");
            var dest = await payoutHandler.ParseClaimDestination(paymentMethod, payoutBlob.Destination, default);
            decimal minimumCryptoAmount =
                await payoutHandler.GetMinimumPayoutAmount(paymentMethod, dest.destination);
            if (cryptoAmount < minimumCryptoAmount)
            {
                req.Completion.TrySetResult(PullPaymentHostedService.PayoutApproval.Result.TooLowAmount);
                return;
            }

            payoutBlob.CryptoAmount = Extensions.RoundUp(cryptoAmount,
                _networkProvider.GetNetwork(paymentMethod.CryptoCode).Divisibility);
            payout.SetBlob(payoutBlob, _jsonSerializerSettings);
            await ctx.SaveChangesAsync();

            req.Completion.SetResult(PullPaymentHostedService.PayoutApproval.Result.Ok);
        }
        catch (Exception ex)
        {
            req.Completion.TrySetException(ex);
        }
    }

    private bool duplicatesExist(decimal amount, string address, ProsusPayDbContext ctx)
    {
        var payouts = ctx.Payouts.Where(data =>
            data.Destination.Equals(address) &&
            data.State != PayoutState.Completed && data.State != PayoutState.Cancelled
        ).ToList();
        return payouts.Any(data => data.GetBlob(_jsonSerializerSettings).Amount == amount);
    }

    class ProsusPayPayoutRequest
    {
        public ProsusPayPayoutRequest(
            TaskCompletionSource<ClaimRequest.ClaimResponse> completionSource,
            ClaimRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(completionSource);
            Completion = completionSource;
            ClaimRequest = request;
        }

        public TaskCompletionSource<ClaimRequest.ClaimResponse> Completion { get; set; }
        public ClaimRequest ClaimRequest { get; }
    }
}
