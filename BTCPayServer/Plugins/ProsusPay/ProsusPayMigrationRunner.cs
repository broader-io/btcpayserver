using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.ProsusPay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.ProsusPay;

public class ProsusPayMigrationRunner : IHostedService
{
    private readonly ProsusPayDbContextFactory _ProsusPayDbContextFactory;

    public ProsusPayMigrationRunner(ProsusPayDbContextFactory ProsusPayDbContextFactory)
    {
        _ProsusPayDbContextFactory = ProsusPayDbContextFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var ctx = _ProsusPayDbContextFactory.CreateContext();
        await ctx.Database.MigrateAsync(cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
