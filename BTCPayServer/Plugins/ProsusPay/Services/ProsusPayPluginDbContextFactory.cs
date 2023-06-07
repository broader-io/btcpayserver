using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.ProsusPay
{
    public class ProsusPayDbContextFactory : BaseDbContextFactory<ProsusPayDbContext>
    {
        public ProsusPayDbContextFactory(IOptions<DatabaseOptions> options) : base(options,
            "BTCPayServer.Plugins.ProsusPay")
        {
        }

        public override ProsusPayDbContext CreateContext()
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
            ConfigureBuilder(builder);
            return new ProsusPayDbContext(builder.Options);
        }
    }
}
