using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helrift.Gate.Contracts.Leaderboards;

namespace Helrift.Gate.Services.Leaderboards;

public sealed class InMemoryLeaderboardRepository : ILeaderboardRepository
{
    private readonly ConcurrentDictionary<string, LeaderboardIncrementData> _events = new();
    private readonly ConcurrentDictionary<LeaderboardCounterKey, long> _counters = new();

    public Task<bool> TryInsertEventAsync(string idempotencyKey, LeaderboardIncrementData data, CancellationToken ct)
    {
        var inserted = _events.TryAdd(idempotencyKey, data);
        return Task.FromResult(inserted);
    }

    public Task UpsertCounterIncrementAsync(LeaderboardCounterKey key, long delta, DateTime updatedUtc, CancellationToken ct)
    {
        _counters.AddOrUpdate(key, delta, (_, existing) => existing + delta);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(LeaderboardCounterKey Key, long Value)>> GetTopAsync(
        string realmId,
        SideType side,
        string metricKey,
        LeaderboardWindowType window,
        DateTime bucketStartUtc,
        int limit,
        CancellationToken ct)
    {
        var rows = _counters
            .Where(kvp =>
                kvp.Key.RealmId == realmId &&
                kvp.Key.Side == side &&
                kvp.Key.MetricKey == metricKey &&
                kvp.Key.Window == window &&
                kvp.Key.BucketStartUtc == bucketStartUtc)
            .OrderByDescending(kvp => kvp.Value)
            .Take(limit)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();

        return Task.FromResult<IReadOnlyList<(LeaderboardCounterKey, long)>>(rows);
    }
}
