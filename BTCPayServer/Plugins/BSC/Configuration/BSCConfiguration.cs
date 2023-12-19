#if ALTCOINS
using System;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BSC.Services;
using BTCPayServer.Services;
using Microsoft.Extensions.Configuration;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;

namespace BTCPayServer.Plugins.BSC.Configuration
{
    public class BSCConfiguration
    {
        public static string SettingsKey(int chainId)
        {
            return $"{nameof(BSCConfiguration)}_{chainId}";
        }

        public int ChainId { get; set; }
        [Display(Name = "Web3 provider url")] public string Web3ProviderUrl { get; set; }

        [Display(Name = "Web3 provider username (can be left blank)")]
        public string Web3ProviderUsername { get; set; }

        [Display(Name = "Web3 provider password (can be left blank)")]
        public string Web3ProviderPassword { get; set; }

        public string LastSeenBlockNumber { get; set; }
        
        public int HighPriorityPollingPeriod { get; set; }
        public int BlockPollingPeriod { get; set; }
        public int PaymentUpdatePollingPeriod { get; set; }
        public int TransactionPollerRange { get; set; }

        public TimeSpan GetHighPriorityPollingPeriod()
        {
            return TimeSpan.FromSeconds(HighPriorityPollingPeriod);
        }
        
        public TimeSpan GetBlockPollingPeriod()
        {
            return TimeSpan.FromSeconds(BlockPollingPeriod);
        }
        
        public TimeSpan GetPaymentUpdatePollingPeriod()
        {
            return TimeSpan.FromSeconds(PaymentUpdatePollingPeriod);
        }

        public BlockParameter GetLastSeenBlockNumber()
        {
            if (!string.IsNullOrEmpty(LastSeenBlockNumber))
            {
                return new BlockParameter(new HexBigInteger(BigInteger.Parse(LastSeenBlockNumber)));
            }

            return new BlockParameter(new HexBigInteger(0));
        }


        private static BSCConfiguration _instance;

        public static async Task<BSCConfiguration> GetInstance(
            SettingsRepository settingsRepository,
            int ChainId,
            bool Load = false
        )
        {
            if (Load || _instance == null)
            {
                _instance = await settingsRepository
                    .GetSettingAsync<BSCConfiguration>(SettingsKey(ChainId));
            }

            return _instance;
        }

        public static async Task<BSCConfiguration> InitializeSettingsFromConfigIfNecessary(
            int chainId,
            IConfiguration configuration,
            SettingsRepository settingsRepository)
        {
            var settings = await settingsRepository.GetSettingAsync<BSCConfiguration>(
                SettingsKey(chainId));

            if (settings != null)
            {
                return settings;
            }

            var val = configuration.GetValue<string>($"chain{chainId}_web3", null);
            var valUser = configuration.GetValue<string>($"chain{chainId}_web3_user", null);
            var valPass = configuration.GetValue<string>($"chain{chainId}_web3_password", null);
            if (val != null)
            {
                return await Create(
                    chainId,
                    val,
                    valUser,
                    valPass,
                    settingsRepository
                );
            }

            return null;
        }

        public static async Task<BSCConfiguration> Create(
            int chainId,
            string web3ProviderUrl,
            string web3ProviderPassword,
            string web3ProviderUsername,
            SettingsRepository settingsRepository,
            int highPriorityPollingPeriod = 10)
        {
            var settings = new BSCConfiguration()
            {
                ChainId = chainId,
                Web3ProviderUrl = web3ProviderUrl,
                Web3ProviderPassword = web3ProviderPassword,
                Web3ProviderUsername = web3ProviderUsername,
                HighPriorityPollingPeriod = highPriorityPollingPeriod
            };

            await settingsRepository.UpdateSetting(
                settings,
                SettingsKey(chainId));

            var web3Utils = new Web3Wrapper(
                chainId,
                settingsRepository
            );

            return settings;
        }

        public override string ToString()
        {
            return "";
        }
    }
}
#endif
