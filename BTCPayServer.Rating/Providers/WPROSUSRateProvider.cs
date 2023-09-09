using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace BTCPayServer.Services.Rates
{

    [Function("balanceOf", "uint256")]
    public class BalanceOf : FunctionMessage
    {
        [Parameter("address", "account", 1)]
        public string account { get; set; }
    }

    public class WPROSUSRateProvider : IRateProvider
    {
        private readonly HttpClient Client;

        public WPROSUSRateProvider(IHttpClientFactory httpClientFactory)
        {
            Client = httpClientFactory.CreateClient();
        }

        public RateSourceInfo RateSourceInfo => new("wprosus", "WPROSUS", "https://rates.prosuspay.com/rates");

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            var poolContractAddress = "0x7D77776BA9ca97004956A0805F206845E772271D".ToLower();
            var url = string.Format(
                "https://app.geckoterminal.com/api/p1/bsc/pools/{0}?base_token=0&include=pairs&fields%5Bpool%5D=pairs&fields%5Bpair%5D=base_price_in_usd%2Cbase_price_in_quote%2Cquote_price_in_usd%2Cquote_price_in_base",
                poolContractAddress);
            var response = await Client.GetAsync(url);
            string responseBody = await response.Content.ReadAsStringAsync();
            var result = JsonDocument.Parse(responseBody);
            var included = result.RootElement.GetProperty("included");
            var attributes = included[0].GetProperty("attributes");
            var base_price_in_usd = attributes.GetProperty("base_price_in_usd");
            var base_price_in_quote = attributes.GetProperty("base_price_in_quote");
            var quote_price_in_usd = attributes.GetProperty("quote_price_in_usd");
            var quote_price_in_base = attributes.GetProperty("quote_price_in_base");
            
            return new List<PairRate>
            {
                new PairRate(new CurrencyPair("WPROSUS", "USDT"), new BidAsk(Decimal.Parse(base_price_in_usd.GetString())))
            }.ToArray();
        }
    }
}
