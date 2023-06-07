#if ALTCOINS
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.ProsusPay
{
    [Route("prosuspayconfig")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class ProsusPayConfigController : Controller
    {
        private readonly SettingsRepository _settingsRepository;

        public ProsusPayConfigController(SettingsRepository settingsRepository)
        {
            _settingsRepository = settingsRepository;
        }

        [HttpGet("")]
        public async Task<IActionResult> GetProsusPayConfig()
        {
            var settings = await _settingsRepository.GetSettingAsync<ProsusPayConfiguration>(ProsusPayConfiguration.SettingsKey());
            if (settings == null)
            {
                settings = new ProsusPayConfiguration();
                await _settingsRepository.UpdateSetting(settings, ProsusPayConfiguration.SettingsKey());
            }
            return View("Plugins/ProsusPay/Views/UpdateConfiguration", settings);
        }

        [HttpPost("")]
        public async Task<IActionResult> UpdateProsusPayConfig(ProsusPayConfiguration vm, string command)
        {
            var settings = await _settingsRepository.GetSettingAsync<ProsusPayConfiguration>(ProsusPayConfiguration.SettingsKey());
            if (settings == null)
            {
                settings = new ProsusPayConfiguration();
                await _settingsRepository.UpdateSetting(settings, ProsusPayConfiguration.SettingsKey());
            }

            if (command == "new")
            {
                settings.CoinConfiguration.Add(new CryptoCodeConfiguration());
                return View("Plugins/ProsusPay/Views/UpdateConfiguration", settings);
            }
            
            if (command == "update")
            {
                await _settingsRepository.UpdateSetting(vm, ProsusPayConfiguration.SettingsKey());
                return View("Plugins/ProsusPay/Views/UpdateConfiguration", settings);
            }

            return null;
        }
    }
}
#endif
