using System;
using System.Globalization;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BSC.Configuration;
using BTCPayServer.Services;
using Nethereum.Contracts;
using Nethereum.Contracts.ContractHandlers;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using System.Numerics;
using Nethereum.Contracts.Standards.ERC20.ContractDefinition;

namespace BTCPayServer.Plugins.BSC.Services;

public class Web3Wrapper
{
    private int _chainId;
    private Web3 _web3;
    private SettingsRepository _settingsRepository;

    public Web3Wrapper(
        int chainId,
        SettingsRepository settingsRepository)
    {
        _chainId = chainId;
        _settingsRepository = settingsRepository;
    }

    public static async Task<Web3Wrapper> GetInstance(
        int chainId,
        SettingsRepository settingsRepository)
    {
        var web3Wrapper = new Web3Wrapper(chainId, settingsRepository);
        await web3Wrapper.Reload();
        return web3Wrapper;
    }

    public async Task Reload()
    {
        var settings = await GetSettings();
        _web3 = GetWeb3Client(settings);
    }

    private async Task<BSCConfiguration> GetSettings()
    {
        return await BSCConfiguration.GetInstance(
            _settingsRepository,
            _chainId,
            true);
    }

    private static Web3 GetWeb3Client(BSCConfiguration settings)
    {
        AuthenticationHeaderValue headerValue = null;
        if (!string.IsNullOrEmpty(settings.Web3ProviderUsername))
        {
            var val = settings.Web3ProviderUsername;
            if (!string.IsNullOrEmpty(settings.Web3ProviderUsername))
            {
                val += $":{settings.Web3ProviderUsername}";
            }

            headerValue = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(
                    Encoding.ASCII.GetBytes(val)));
        }

        return new Web3(settings.Web3ProviderUrl, null, headerValue);
    }

    public ContractHandler GetContract(BEP20BTCPayNetwork network)
    {
        var address = network.SmartContractAddress;
        return _web3.Eth.GetContractHandler(address);
    }

    public async Task EnsureChain(int chainId)
    {
        var result = await _web3.Eth.ChainId.SendRequestAsync();
        if (result.Value != chainId)
        {
            throw new Exception(
                $"The web3 client is connected to a different chain id. Expected {chainId} but Web3 returned {result.Value}");
        }
    }

    public async Task<BlockParameter> GetLatestBlockNumber()
    {
        var block = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
        return new BlockParameter(block);
    }
    
    public async Task<long> GetLatestBlockNumberLong()
    {
        var block = await GetLatestBlockNumber();
        return (long)block.BlockNumber.Value;
    }

    public async Task<BlockParameter> GetLastSeenBlockNumberFromSettings()
    {
        var settings = await GetSettings();
        return await Task.FromResult(
            new BlockParameter(
                new HexBigInteger(settings.LastSeenBlockNumber)));
    }

    public async Task UpdateLastSeenBlockNumberSettings(long blockNumber)
    {
        var settings = await GetSettings();
        settings.LastSeenBlockNumber = blockNumber.ToString();

        await _settingsRepository.UpdateSetting(
            settings,
            BSCConfiguration.SettingsKey(settings.ChainId));
    }

    public Event<TransferEventDTO> GetTransferEventDTOEvent(string contractAddress)
    {
        return _web3.Eth.GetEvent<TransferEventDTO>();
    }

    public async Task<BigInteger> GetBalance(
        string address,
        BSCBTCPayNetwork network)
    {
        if (network is BEP20BTCPayNetwork bep20Network)
        {
            return await GetContract(bep20Network)
                .QueryAsync<BalanceOfFunction, BigInteger>(
                    new BalanceOfFunction() {Owner = address});
        }

        return await _web3.Eth.GetBalance
            .SendRequestAsync(address, GetLatestBlockNumber)
            .ContinueWith(t => t.Result);
    }

    public async Task<decimal> GetNextFeeRate(BSCBTCPayNetwork network)
    {
        var feeTask = await _web3
            .FeeSuggestion
            .GetSimpleFeeSuggestionStrategy()
            .SuggestFeeAsync();
        var fee = feeTask.BaseFee.GetValueOrDefault(0);
        return decimal.Parse(
            Web3.Convert.FromWeiToBigDecimal(fee, network.Divisibility).ToString(),
            CultureInfo.InvariantCulture);
    }

    public async Task<Transaction> GetTransaction(string hash)
    {
        return  await _web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(hash);
    }
    
    [Event("Transfer")]
    public class TransferEventDTO : IEventDTO
    {
        [Parameter("address", "_from", 1, true)]
        public string From { get; set; }

        [Parameter("address", "_to", 2, true)]
        public string To { get; set; }

        [Parameter("uint256", "_value", 3, false)]
        public BigInteger Value { get; set; }
    }
}
