#if ALTCOINS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.BSC.Payments;
using BTCPayServer.Plugins.BSC.Services;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Nethereum.Web3;

namespace BTCPayServer.Plugins.BSC.Payments
{
    public class
        BSCPaymentMethodHandler : PaymentMethodHandlerBase<BSCSupportedPaymentMethod,
            BSCBTCPayNetwork>
    {
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly BSCService _BSCService;

        public BSCPaymentMethodHandler(BTCPayNetworkProvider networkProvider, BSCService BSCService)
        {
            _networkProvider = networkProvider;
            _BSCService = BSCService;
        }

        public override PaymentType PaymentType => BSCPaymentType.Instance;

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(InvoiceLogs logs,
            BSCSupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod,
            StoreData store, BSCBTCPayNetwork network, object preparePaymentObject,
            IEnumerable<PaymentMethodId> invoicePaymentMethods)
        {
            if (preparePaymentObject is null)
            {
                return new BSCOnChainPaymentMethodDetails() {Activated = false};
            }

            if (!_BSCService.IsAvailable(network.CryptoCode, out var error))
                throw new PaymentMethodUnavailableException(error ?? $"Not configured yet");
            var invoice = paymentMethod.ParentEntity;
            if (!(preparePaymentObject is Prepare ethPrepare)) throw new ArgumentException();
            var address = await ethPrepare.ReserveAddress(invoice.Id);
            if (address is null || address.Failed)
            {
                throw new PaymentMethodUnavailableException($"could not generate address");
            }

            return new BSCOnChainPaymentMethodDetails()
            {
                DepositAddress = address.Address, Index = address.Index, XPub = address.XPub, Activated = true
            };
        }

        public override object PreparePayment(BSCSupportedPaymentMethod supportedPaymentMethod, StoreData store,
            BTCPayNetworkBase network)
        {
            return new Prepare()
            {
                ReserveAddress = s =>
                    _BSCService.ReserveNextAddress(
                        new BSCService.ReserveBSCAddress() {StoreId = store.Id, CryptoCode = network.CryptoCode})
            };
        }

        class Prepare
        {
            public Func<string, Task<BSCService.ReserveBSCAddressResponse>> ReserveAddress;
        }

        // public override Task<IPaymentMethodDetails> CreatePaymentMethodDetails(InvoiceLogs logs, BSCSupportedPaymentMethod supportedPaymentMethod,
        //     PaymentMethod paymentMethod, StoreData store, BSCBTCPayNetwork network, object preparePaymentObject,
        //     IEnumerable<PaymentMethodId> invoicePaymentMethods)
        // {
        //     throw new NotImplementedException();
        // }

        public override void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse,
            StoreBlob storeBlob, IPaymentMethod paymentMethod)
        {
            var satoshiCulture = new CultureInfo(CultureInfo.InvariantCulture.Name)
            {
                NumberFormat = { NumberGroupSeparator = " " }
            };
            
            var divisbility = ((PaymentMethod)paymentMethod).Network.Divisibility;
            var paymentMethodId = paymentMethod.GetId();
            var cryptoInfo = invoiceResponse.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
            var amountWei = Web3.Convert.ToWei(Convert.ToDecimal(model.BtcDue, new CultureInfo("en-US")), divisbility);
            //var url = $"bep20:{cryptoInfo.Address}?value={amountWei}";
            var url = cryptoInfo.Address;
            var network = _networkProvider.GetNetwork<BSCBTCPayNetwork>(model.CryptoCode);
            model.PaymentMethodName = GetPaymentMethodName(network);
            model.CryptoImage = GetCryptoImage(network);
            model.InvoiceBitcoinUrl = "";
            model.InvoiceBitcoinUrlQR = url ?? "";
            //model.BtcDue = amountWei.ToString(new CultureInfo("en-US"));
            //model.BtcDue = Money.Parse(model.BtcDue).ToUnit(MoneyUnit.Satoshi).ToString(satoshiCulture);
        }

        public override string GetCryptoImage(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<BSCBTCPayNetwork>(paymentMethodId.CryptoCode);
            return GetCryptoImage(network);
        }

        public override string GetPaymentMethodName(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<BSCBTCPayNetwork>(paymentMethodId.CryptoCode);
            return GetPaymentMethodName(network);
        }

        public override IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
        {
            return _networkProvider.GetAll().OfType<BSCBTCPayNetwork>()
                .Select(network => new PaymentMethodId(network.CryptoCode, PaymentType));
        }

        public override CheckoutUIPaymentMethodSettings GetCheckoutUISettings()
        {
            return new CheckoutUIPaymentMethodSettings()
            {
                ExtensionPartial = "Plugins/BSC/Views/Shared/BSCMethodCheckout",
                CheckoutBodyVueComponentName = "BSCMethodCheckout",
                CheckoutHeaderVueComponentName = "BSCMethodCheckoutHeader",
                NoScriptPartialName = "Bitcoin/BitcoinMethodCheckoutNoScript"
            };
        }

        private string GetCryptoImage(BSCBTCPayNetwork network)
        {
            return network.CryptoImagePath;
        }

        private string GetPaymentMethodName(BSCBTCPayNetwork network)
        {
            return $"{network.DisplayName}";
        }
    }
}
#endif
