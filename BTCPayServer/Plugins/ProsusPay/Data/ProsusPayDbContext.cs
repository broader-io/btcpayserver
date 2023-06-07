using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.ProsusPay
{
    public class ProsusPayDbContext : ApplicationDbContext
    {
        private readonly bool _designTime;

        public DbSet<ProsusPayPaymentsData> ProsusPayPayments { get; set; }

        public ProsusPayDbContext(DbContextOptions<ApplicationDbContext> options, bool designTime = false)
            : base(options)
        {
            _designTime = designTime;
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            ProsusPayPaymentsData.OnModelCreating(builder);
        }
    }
}
