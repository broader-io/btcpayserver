using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.ProsusPay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Data
{
    public class ProsusPayDbContextFactory : BaseDbContextFactory<ProsusPayDbContext>
    {
        public ProsusPayDbContextFactory(IOptions<DatabaseOptions> options) : base(options, "")
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
