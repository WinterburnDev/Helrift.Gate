using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Helrift.Gate.Contracts.Leaderboards;

namespace Helrift.Gate.Services.Leaderboards;

public sealed record LeaderboardCounterKey(
    string RealmId,
    SideType Side,
    string MetricKey,
    LeaderboardWindowType Window,
    DateTime BucketStartUtc,
    LeaderboardSubjectType SubjectType,
    string SubjectId
);

public interface ILeaderboardRepository
{
    /// <summary>
    /// Insert event by idempotency key. Return false if already exists (duplicate).
    /// </summary>
    Task<bool> TryInsertEventAsync(string idempotencyKey, LeaderboardIncrementData data, CancellationToken ct);

    Task UpsertCounterIncrementAsync(LeaderboardCounterKey key, long delta, DateTime updatedUtc, CancellationToken ct);

    Task<IReadOnlyList<(LeaderboardCounterKey Key, long Value)>> GetTopAsync(
        string realmId,
        SideType side,
        string metricKey,
        LeaderboardWindowType window,
        DateTime bucketStartUtc,
        int limit,
        CancellationToken ct);
}
