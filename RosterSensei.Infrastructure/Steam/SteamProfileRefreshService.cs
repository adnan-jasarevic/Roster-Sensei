using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RosterSensei.Infrastructure.Steam;

// Runs SteamProfileRefresher every few minutes. Each account is only looked up once a week; running often
// just spreads the lookups out (Steam rate-limits bursts) and picks up new links quickly.
// Safe with several API instances: accounts are claimed in the database before they're looked up.
public sealed class SteamProfileRefreshService : BackgroundService
{
    public static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Period = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<SteamProfileRefreshService> _logger;

    public SteamProfileRefreshService(IServiceScopeFactory scopes, TimeProvider time, ILogger<SteamProfileRefreshService> logger)
    {
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRunDelay, _time, stoppingToken);
            using var timer = new PeriodicTimer(Period, _time);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The API is shutting down.
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            // A scope per pass: the DbContext and its connection go back to the pool as soon as the pass ends.
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SteamProfileRefresher>().RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed pass (database briefly down, say) must not stop the job; the next pass tries again.
            _logger.LogError(ex, "Steam profile refresh failed.");
        }
    }
}
