#if ALTCOINS
using System.Net.Http;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.BSC.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.BSC
{
    public class BSCPlugin : BaseBTCPayServerPlugin
    {
        
        public override string Identifier => "BTCPayServer.Plugins.BSC";
        public override string Name => "Binance Smart Chain";
        public override string Description => "Allows you to integrate Binance Smart Chain.";

        
        public  const string BSCInvoiceCheckHttpClient = "BSCCheck";
        public  const string BSCInvoiceCreateHttpClient = "BSCCreate";
        public override void Execute(IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<BSCService>();
            serviceCollection.AddSingleton<IHostedService, BSCService>(provider => provider.GetService<BSCService>());
            serviceCollection.AddSingleton<BSCPaymentMethodHandler>();
            serviceCollection.AddSingleton<IPaymentMethodHandler>(provider => provider.GetService<BSCPaymentMethodHandler>());
            
            serviceCollection.AddSingleton<IUIExtension>(new UIExtension("Plugins/BSC/Views/Shared/StoreNavBSCExtension",  "store-nav"));
            serviceCollection.AddTransient<NoRedirectHttpClientHandler>();
            serviceCollection.AddSingleton<ISyncSummaryProvider, BSCSyncSummaryProvider>();
            serviceCollection.AddHttpClient(BSCInvoiceCreateHttpClient)
                .ConfigurePrimaryHttpMessageHandler<NoRedirectHttpClientHandler>();
            base.Execute(serviceCollection);
        }
    }
    
    public class NoRedirectHttpClientHandler : HttpClientHandler
    {
        public NoRedirectHttpClientHandler()
        {
            this.AllowAutoRedirect = false;
        }
    }
}
#endif
