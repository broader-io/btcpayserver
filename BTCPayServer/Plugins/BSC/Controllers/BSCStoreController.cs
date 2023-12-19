#if ALTCOINS
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using Nethereum.HdWallet;
using Nethereum.Hex.HexConvertors.Extensions;

namespace BTCPayServer.Plugins.BSC.Controllers
{
    [Route("stores/{storeId}/bsc")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class BSCStoreController : Controller
    {
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        public BSCStoreController(StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
        }

        private StoreData StoreData => HttpContext.GetStoreData();

        [HttpGet()]
        public IActionResult GetStoreBSCPaymentMethods()
        {
            var eth = StoreData.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<BSCSupportedPaymentMethod>();

            var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
            var ethNetworks = _btcPayNetworkProvider.GetAll().OfType<BSCBTCPayNetwork>();

            var vm = new ViewBSCStoreOptionsViewModel() { };

            foreach (var network in ethNetworks)
            {
                var paymentMethodId = new PaymentMethodId(network.CryptoCode, BSCPaymentType.Instance);
                var matchedPaymentMethod = eth.SingleOrDefault(method =>
                    method.PaymentId == paymentMethodId);
                vm.Items.Add(new ViewBSCStoreOptionItemViewModel()
                {
                    Label = matchedPaymentMethod?.Label,
                    CryptoCode = network.CryptoCode,
                    Enabled = matchedPaymentMethod != null && !excludeFilters.Match(paymentMethodId),
                    IsToken = network is BEP20BTCPayNetwork,
                    RootAddress = matchedPaymentMethod?.GetDerivedAddress()?.Invoke(0) ?? "not configured"
                });
            }

            return View("Plugins/BSC/Views/GetStoreBSCPaymentMethods", vm);
        }

        [HttpGet("{cryptoCode}")]
        public IActionResult GetStoreBSCPaymentMethod(string cryptoCode)
        {
            var eth = StoreData.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<BSCSupportedPaymentMethod>();

            var network = _btcPayNetworkProvider.GetNetwork<BSCBTCPayNetwork>(cryptoCode);
            if (network is null)
            {
                return NotFound();
            }

            var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
            var paymentMethodId = new PaymentMethodId(network.CryptoCode, BSCPaymentType.Instance);
            var matchedPaymentMethod = eth.SingleOrDefault(method =>
                method.PaymentId == paymentMethodId);

            return View("Plugins/BSC/Views/GetStoreBSCPaymentMethod", new EditBSCPaymentMethodViewModel()
            {
                Label = matchedPaymentMethod?.Label,
                Enabled = !excludeFilters.Match(paymentMethodId),
                AccountDerivation = matchedPaymentMethod?.AccountDerivation,
                Index = matchedPaymentMethod?.CurrentIndex ?? 0,
                OriginalIndex = matchedPaymentMethod?.CurrentIndex ?? 0,
                AccountKeyPath = string.IsNullOrEmpty(matchedPaymentMethod?.GetKeyPath())
                    ? network.GetDefaultKeyPath()
                    : matchedPaymentMethod?.GetKeyPath(),
                RootAddress = matchedPaymentMethod?.GetDerivedAddress()?.Invoke(0) ?? "not configured"
            });
        }

        [HttpPost("{cryptoCode}")]
        public async Task<IActionResult> GetStoreBSCPaymentMethod(
            string cryptoCode,
            EditBSCPaymentMethodViewModel viewModel)
        {
            var network = _btcPayNetworkProvider.GetNetwork<BSCBTCPayNetwork>(cryptoCode);
            if (network is null)
            {
                return NotFound();
            }

            var store = StoreData;
            var blob = StoreData.GetStoreBlob();
            var paymentMethodId = new PaymentMethodId(network.CryptoCode, BSCPaymentType.Instance);

            var currentPaymentMethod = StoreData.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<BSCSupportedPaymentMethod>().SingleOrDefault(method =>
                    method.PaymentId == paymentMethodId);

            // if (currentPaymentMethod != null && currentPaymentMethod.CurrentIndex != viewModel.Index &&
            //     viewModel.OriginalIndex == viewModel.Index)
            // {
            //     viewModel.Index = currentPaymentMethod.CurrentIndex;
            //     viewModel.OriginalIndex = currentPaymentMethod.CurrentIndex;
            // }
            // else if (currentPaymentMethod != null && currentPaymentMethod.CurrentIndex != viewModel.Index &&
            //          viewModel.OriginalIndex != currentPaymentMethod.CurrentIndex)
            // {
            //     ModelState.AddModelError(nameof(viewModel.Index),
            //         $"You tried to update the index (to {viewModel.Index}) but new derivations in the background updated the index (to {currentPaymentMethod.CurrentIndex}) ");
            //     viewModel.Index = currentPaymentMethod.CurrentIndex;
            //     viewModel.OriginalIndex = currentPaymentMethod.CurrentIndex;
            // }


            Wallet wallet = null;
            try
            {
                if (!string.IsNullOrEmpty(viewModel.Seed))
                {
                    wallet = new Wallet(viewModel.Seed, viewModel.Passphrase,
                        string.IsNullOrEmpty(viewModel.AccountKeyPath)
                            ? network.GetDefaultKeyPath()
                            : viewModel.AccountKeyPath);
                    string xpub1 = wallet.GetMasterExtPubKey().GetWif(Network.Main).ToWif();
                    Console.WriteLine(xpub1);
                    string xpub2 = wallet.GetMasterExtKey().GetWif(Network.Main).ToWif();
                    Console.WriteLine(xpub2);
                    // string xpub3 = wallet.GetExtPubKey(0).ToString();
                    // Console.WriteLine(xpub3);
                    // string xpub4 = wallet.GetMasterExtPubKey().ToString();
                    // Console.WriteLine(xpub4);
                    viewModel.AccountDerivation = wallet.GetMasterExtPubKey().GetWif(Network.Main).ToWif();
                }
            }
            catch (Exception)
            {
                ModelState.AddModelError(nameof(viewModel.Seed), $"seed was incorrect");
            }

            if (wallet != null)
            {
                try
                {
                    wallet.GetAccount(0);
                }
                catch (Exception)
                {
                    ModelState.AddModelError(nameof(viewModel.AccountKeyPath), $"keypath was incorrect");
                }
            }

            PublicWallet publicWallet = null;
            try
            {
                if (!string.IsNullOrEmpty(viewModel.AccountDerivation))
                {
                    try
                    {
                        publicWallet = new PublicWallet(viewModel.AccountDerivation);
                    }
                    catch (Exception)
                    {
                        publicWallet = new PublicWallet(new BitcoinExtPubKey(viewModel.AccountDerivation, Network.Main)
                            .ExtPubKey);
                    }

                    if (wallet != null && !publicWallet.ExtPubKey.Equals(wallet.GetMasterPublicWallet().ExtPubKey))
                    {
                        ModelState.AddModelError(nameof(viewModel.AccountDerivation),
                            $"The xpub does not match the seed/pass/key path provided");
                    }
                }
            }
            catch (Exception)
            {
                ModelState.AddModelError(nameof(viewModel.AccountDerivation), $"xpub was incorrect");
            }

            if (!string.IsNullOrEmpty(viewModel.AddressCheck))
            {
                int index = -1;
                if (wallet != null)
                {
                    index = Array.IndexOf(wallet.GetAddresses(1000), viewModel.AddressCheck);
                }
                else if (publicWallet != null)
                {
                    index = Array.IndexOf(publicWallet.GetAddresses(1000), viewModel.AddressCheck);
                }

                if (viewModel.AddressCheckLastUsed && index > -1)
                {
                    viewModel.Index = index;
                }

                if (index == -1)
                {
                    ModelState.AddModelError(nameof(viewModel.AddressCheck),
                        "Could not confirm address belongs to configured wallet");
                }
            }

            if (!ModelState.IsValid)
            {
                return View("Plugins/BSC/Views/GetStoreBSCPaymentMethod", viewModel);
            }

            var derivationScheme = string.IsNullOrEmpty(viewModel.AccountDerivation) && wallet != null
                ? wallet.GetMasterPublicWallet().ExtPubKey.ToBytes().ToHex()
                : viewModel.AccountDerivation;

            // var derivationScheme = new DirectDerivationStrategy(
            //     new BitcoinExtPubKey(xpub, Network.Main),
            //     false, null
            // );
            // var derivation = new DerivationSchemeSettings(derivationScheme, network);
            // var accountKeySettings = (AccountKeySettings)derivation.AccountKeySettings.GetValue(0);
            var keyPath = string.IsNullOrEmpty(viewModel.AccountKeyPath)
                ? network.GetDefaultKeyPath()
                : viewModel.AccountKeyPath;
            // accountKeySettings.AccountKeyPath = KeyPath.Parse(keyPath);

            var currentIndex = currentPaymentMethod?.CurrentIndex ?? 0;
            currentPaymentMethod ??= new BSCSupportedPaymentMethod();
            currentPaymentMethod.Label = viewModel.Label;
            currentPaymentMethod.Source = "BTCPayStoreSettings";
            currentPaymentMethod.AccountDerivation = viewModel.AccountDerivation;
            currentPaymentMethod.CurrentIndex = currentIndex;
            currentPaymentMethod.CryptoCode = cryptoCode;
            if (currentPaymentMethod.AccountKeySettings.Length == 0)
            {
                currentPaymentMethod.AccountKeySettings = currentPaymentMethod.AccountKeySettings.Append(
                    new AccountKeySettings
                    {
                        AccountKey = viewModel.AccountDerivation, AccountKeyPath = viewModel.AccountKeyPath
                    }).ToArray();
            }
            else
            {
                currentPaymentMethod.AccountKeySettings[0].AccountKey = viewModel.AccountDerivation;
                currentPaymentMethod.AccountKeySettings[0].AccountKeyPath = viewModel.AccountKeyPath;
            }


            //var accountKeySettings = (AccountKeySettings)currentPaymentMethod.AccountKeySettings.GetValue(0);
            //accountKeySettings.AccountKeyPath = KeyPath.Parse(keyPath);
            // currentPaymentMethod.Password = viewModel.StoreSeed ? viewModel.Passphrase : "";
            // currentPaymentMethod.Seed = viewModel.StoreSeed ? viewModel.Seed : "";
            // // currentPaymentMethod.XPub = string.IsNullOrEmpty(viewModel.XPub) && wallet != null
            // //     ? wallet.GetMasterPublicWallet().ExtPubKey.ToBytes().ToHex()
            // //     : viewModel.XPub;
            // currentPaymentMethod.AccountDerivation = viewModel.XPub;
            // //currentPaymentMethod.CryptoCode = cryptoCode;
            // currentPaymentMethod.accountKeyPath = string.IsNullOrEmpty(viewModel.KeyPath)
            //     ? network.GetDefaultKeyPath()
            //     : viewModel.KeyPath;
            // currentPaymentMethod.CurrentIndex = viewModel.Index;

            blob.SetExcluded(paymentMethodId, !viewModel.Enabled);
            store.SetSupportedPaymentMethod(paymentMethodId, currentPaymentMethod);
            store.SetStoreBlob(blob);
            await _storeRepository.UpdateStore(store);

            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"updated {cryptoCode}", Severity = StatusMessageModel.StatusSeverity.Success
            });

            return RedirectToAction("GetStoreBSCPaymentMethods", new {storeId = store.Id});
        }
    }

    public class EditBSCPaymentMethodViewModel
    {
        public string Label { get; set; }

        [Display(Name = "XPUB")] public string AccountDerivation { get; set; }
        public string Seed { get; set; }
        public string Passphrase { get; set; }

        public string AccountKeyPath { get; set; }
        public long OriginalIndex { get; set; }

        [Display(Name = "Root Address")] public string RootAddress { get; set; }

        [Display(Name = "Current address index")]
        public long Index { get; set; }

        public bool Enabled { get; set; }

        [Display(Name = "Address Check")] public string AddressCheck { get; set; }

        public bool AddressCheckLastUsed { get; set; }
    }

    public class ViewBSCStoreOptionsViewModel
    {
        public List<ViewBSCStoreOptionItemViewModel> Items { get; set; } =
            new List<ViewBSCStoreOptionItemViewModel>();
    }

    public class ViewBSCStoreOptionItemViewModel
    {
        public string Label { get; set; }
        public string CryptoCode { get; set; }
        public bool IsToken { get; set; }
        public bool Enabled { get; set; }
        public string RootAddress { get; set; }
    }
}
#endif
