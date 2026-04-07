// Services/TownProjects/TownProjectRotationHostedService.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Helrift.Gate.Api.Services.TownProjects;

/// <summary>
/// Background service that fires the weekly town project rotation on a schedule.
/// The reset day and hour are configurable via TownProjects:WeeklyResetDayOfWeek
/// (default: Monday) and TownProjects:WeeklyResetHourUtc (default: 0).
/// On startup it performs a catch-up reset if the scheduled time has passed within
/// the current week and no reset has been fired yet in this process lifetime.
/// </summary>
public sealed class TownProjectRotationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TownProjectRotationHostedService> _log;
    private readonly DayOfWeek _resetDayOfWeek;
    private readonly int _resetHourUtc;

    // Tracks the last reset fired in this process to avoid double-firing on restart.
    private DateTime _lastResetFiredUtc = DateTime.MinValue;

    public TownProjectRotationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<TownProjectRotationHostedService> log,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _log = log;

        var dayStr = configuration["TownProjects:WeeklyResetDayOfWeek"] ?? "Monday";
        _resetDayOfWeek = Enum.TryParse<DayOfWeek>(dayStr, ignoreCase: true, out var d) ? d : DayOfWeek.Monday;
        _resetHourUtc = configuration.GetValue<int>("TownProjects:WeeklyResetHourUtc", 0);

        _log.LogInformation(
            "TownProjectRotationHostedService configured: weekly reset on {DayOfWeek} at {Hour:D2}:00 UTC",
            _resetDayOfWeek, _resetHourUtc);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief startup delay to allow all singletons (Firebase config, etc.) to initialise.
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        // Catch-up: fire immediately if we missed the scheduled reset this week.
        var lastScheduled = GetLastScheduledResetUtc();
        if (DateTime.UtcNow >= lastScheduled && _lastResetFiredUtc < lastScheduled)
        {
            _log.LogInformation(
                "TownProjectRotationHostedService: catch-up reset detected (last scheduled: {LastScheduled:u})",
                lastScheduled);
            await FireWeeklyResetAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = GetNextScheduledResetUtc();
            var delay = next - DateTime.UtcNow;

            _log.LogInformation(
                "TownProjectRotationHostedService: next weekly reset scheduled at {Next:u} (in {Hours:F1}h)",
                next, delay.TotalHours);

            try
            {
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await FireWeeklyResetAsync(stoppingToken);
        }
    }

    private async Task FireWeeklyResetAsync(CancellationToken ct)
    {
        _log.LogInformation("TownProjectRotationHostedService: executing weekly reset");
        _lastResetFiredUtc = DateTime.UtcNow;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var rotation = scope.ServiceProvider.GetRequiredService<ITownProjectRotationService>();
            await rotation.ExecuteWeeklyResetAsync(ct);
            _log.LogInformation("TownProjectRotationHostedService: weekly reset completed");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "TownProjectRotationHostedService: weekly reset failed");
        }
    }

    /// <summary>Returns the most recent past occurrence of the configured reset time.</summary>
    private DateTime GetLastScheduledResetUtc()
    {
        var now = DateTime.UtcNow;
        var daysBack = ((int)now.DayOfWeek - (int)_resetDayOfWeek + 7) % 7;
        var candidate = now.Date.AddDays(-daysBack).AddHours(_resetHourUtc);
        // If today is the reset day but the hour hasn't passed yet, go back 7 days.
        return candidate <= now ? candidate : candidate.AddDays(-7);
    }

    /// <summary>Returns the next future occurrence of the configured reset time.</summary>
    private DateTime GetNextScheduledResetUtc() => GetLastScheduledResetUtc().AddDays(7);
}
