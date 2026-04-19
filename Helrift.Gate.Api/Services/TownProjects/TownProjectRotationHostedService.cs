// Services/TownProjects/TownProjectRotationHostedService.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Helrift.Gate.App.Repositories;

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
    private readonly string _realmId;
    private readonly TimeSpan _leaseDuration;
    private readonly string _leaseOwnerId;

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
        _realmId = configuration["RealmId"] ?? "default";
        var leaseMinutes = configuration.GetValue<int>("TownProjects:WeeklyResetLeaseMinutes", 30);
        _leaseDuration = TimeSpan.FromMinutes(Math.Max(5, leaseMinutes));
        _leaseOwnerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

        _log.LogInformation(
            "TownProjectRotationHostedService configured: weekly reset on {DayOfWeek} at {Hour:D2}:00 UTC",
            _resetDayOfWeek, _resetHourUtc);

        _log.LogInformation(
            "TownProjectRotationHostedService lease owner: {LeaseOwnerId}, lease duration: {LeaseDurationMinutes}m",
            _leaseOwnerId,
            _leaseDuration.TotalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief startup delay to allow all singletons (Firebase config, etc.) to initialise.
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        // Catch-up: fire immediately if we missed the scheduled reset this week.
        var lastScheduled = GetLastScheduledResetUtc();
        if (DateTime.UtcNow >= lastScheduled)
        {
            await TryExecuteResetSlotAsync(lastScheduled, "catch-up", stoppingToken);
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

            await TryExecuteResetSlotAsync(next, "scheduled", stoppingToken);
        }
    }

    private async Task TryExecuteResetSlotAsync(DateTime scheduledSlotUtc, string reason, CancellationToken ct)
    {
        var leaseAcquired = false;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var stateRepo = scope.ServiceProvider.GetRequiredService<ITownProjectStateRepository>();

            var persistedSlot = await stateRepo.GetLastWeeklyResetSlotUtcAsync(_realmId, ct);
            var alreadyProcessed = (_lastResetFiredUtc >= scheduledSlotUtc) ||
                                   (persistedSlot.HasValue && persistedSlot.Value >= scheduledSlotUtc);

            if (alreadyProcessed)
            {
                _log.LogInformation(
                    "TownProjectRotationHostedService: skipping {Reason} reset for slot {Slot:u} (process marker: {ProcessMarker:u}, persisted marker: {PersistedMarker:u})",
                    reason,
                    scheduledSlotUtc,
                    _lastResetFiredUtc,
                    persistedSlot);
                return;
            }

            leaseAcquired = await stateRepo.TryAcquireWeeklyResetLeaseAsync(
                _realmId,
                _leaseOwnerId,
                DateTime.UtcNow,
                _leaseDuration,
                ct);

            if (!leaseAcquired)
            {
                _log.LogInformation(
                    "TownProjectRotationHostedService: skipping {Reason} reset for slot {Slot:u}; lease held by another instance",
                    reason,
                    scheduledSlotUtc);
                return;
            }

            // Re-check persisted slot after lease acquisition to avoid duplicate work.
            persistedSlot = await stateRepo.GetLastWeeklyResetSlotUtcAsync(_realmId, ct);
            if (persistedSlot.HasValue && persistedSlot.Value >= scheduledSlotUtc)
            {
                _log.LogInformation(
                    "TownProjectRotationHostedService: skipping {Reason} reset for slot {Slot:u}; slot already persisted after lease acquisition ({PersistedMarker:u})",
                    reason,
                    scheduledSlotUtc,
                    persistedSlot.Value);
                return;
            }

            _log.LogInformation(
                "TownProjectRotationHostedService: executing {Reason} reset for slot {Slot:u}",
                reason,
                scheduledSlotUtc);

            var success = await FireWeeklyResetAsync(ct);
            if (!success)
                return;

            _lastResetFiredUtc = scheduledSlotUtc;
            var persisted = await stateRepo.SaveLastWeeklyResetSlotUtcAsync(_realmId, scheduledSlotUtc, ct);
            if (!persisted)
            {
                _log.LogWarning(
                    "TownProjectRotationHostedService: weekly reset succeeded for slot {Slot:u} but failed to persist slot marker",
                    scheduledSlotUtc);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "TownProjectRotationHostedService: failed to evaluate/execute {Reason} reset for slot {Slot:u}",
                reason,
                scheduledSlotUtc);
        }
        finally
        {
            if (leaseAcquired)
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var stateRepo = scope.ServiceProvider.GetRequiredService<ITownProjectStateRepository>();
                    await stateRepo.ReleaseWeeklyResetLeaseAsync(_realmId, _leaseOwnerId, ct);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex,
                        "TownProjectRotationHostedService: failed to release weekly reset lease for owner {LeaseOwnerId}",
                        _leaseOwnerId);
                }
            }
        }
    }

    private async Task<bool> FireWeeklyResetAsync(CancellationToken ct)
    {
        _log.LogInformation("TownProjectRotationHostedService: executing weekly reset");

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var rotation = scope.ServiceProvider.GetRequiredService<ITownProjectRotationService>();
            await rotation.ExecuteWeeklyResetAsync(ct);
            _log.LogInformation("TownProjectRotationHostedService: weekly reset completed");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "TownProjectRotationHostedService: weekly reset failed");
            return false;
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
