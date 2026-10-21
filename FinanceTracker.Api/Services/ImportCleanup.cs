using FinanceTracker.Api.Data;
using Microsoft.EntityFrameworkCore;
namespace FinanceTracker.Api.Services;
public class ImportCleanup(IServiceScopeFactory scopes, ILogger<ImportCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.ImportBatches.Where(b => b.ConfirmedAt == null && b.ExpiresAt <= DateTime.UtcNow).ExecuteDeleteAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning(ex, "Expired preview cleanup failed; it will retry."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
