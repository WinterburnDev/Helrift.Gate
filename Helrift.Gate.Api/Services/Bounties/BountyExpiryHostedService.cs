using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Helrift.Gate.Api.Services.Bounties;

public sealed class BountyExpiryHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BountyExpiryHostedService> _logger;
    private readonly string _realmId;
    private readonly TimeSpan _interval;

    public BountyExpiryHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<BountyExpiryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        _realmId = configuration["RealmId"] ?? "default";
        var seconds = configuration.GetValue<int>("Bounty:ExpirySweepSeconds", 60);
        _interval = TimeSpan.FromSeconds(Math.Max(15, seconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var bounties = scope.ServiceProvider.GetRequiredService<IBountyService>();
                var expired = await bounties.ExpireDueBountiesAsync(_realmId, stoppingToken);

                if (expired > 0)
                {
                    _logger.LogInformation(
                        "Bounty expiry sweep complete. realm={RealmId} expired={ExpiredCount}",
                        _realmId,
                        expired);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Bounty expiry sweep failed. realm={RealmId}",
                    _realmId);
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
