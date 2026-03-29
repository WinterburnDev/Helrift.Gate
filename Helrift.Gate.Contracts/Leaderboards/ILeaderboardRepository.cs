using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Helrift.Gate.Contracts.Leaderboards;

public enum LeaderboardWindowType : byte
{
    Daily = 1,
    Weekly = 2,
    Monthly = 3
}

public enum LeaderboardSubjectType : byte
{
    Character = 1
}

// Your Character.side is Aresden/Elvine
public enum SideType : byte
{
    Aresden = 1,
    Elvine = 2
}

public sealed record LeaderboardIncrementData(
    string IdempotencyKey,
    DateTime OccurredUtc,
    string RealmId,
    SideType Side,
    string MetricKey,
    LeaderboardSubjectType SubjectType,
    string SubjectId,
    int Delta,
    string SourceGameServerId
);

public sealed record LeaderboardCounterKey(
    string RealmId,
    SideType Side,
    string MetricKey,
    LeaderboardWindowType Window,
    DateTime BucketStartUtc,
    LeaderboardSubjectType SubjectType,
    string SubjectId
);

public sealed record LeaderboardEntryDto(
    int Rank,
    string SubjectId,
    string DisplayName,
    long Value
);

public sealed record LeaderboardResponseDto(
    string RealmId,
    SideType Side,
    string MetricKey,
    LeaderboardWindowType Window,
    DateTime BucketStartUtc,
    IReadOnlyList<LeaderboardEntryDto> Items
);

public sealed record GetLeaderboardQuery(
    string RealmId,
    SideType Side,
    string MetricKey,
    LeaderboardWindowType Window,
    int Limit,
    DateTime? BucketStartUtc // null => current bucket
);

public interface ILeaderboardRepository
{
    Task<bool> TryInsertEventAsync(string idempotencyKey, LeaderboardIncrementData data, CancellationToken ct);
    Task UpsertCounterIncrementAsync(LeaderboardCounterKey key, long delta, DateTime updatedUtc, CancellationToken ct);
    Task<IReadOnlyList<(LeaderboardCounterKey Key, long Value)>> GetTopAsync(
        string realmId, SideType side, string metricKey, LeaderboardWindowType window,
        DateTime bucketStartUtc, int limit, CancellationToken ct);

    /// <summary>Returns all distinct metric keys currently tracked.</summary>
    IReadOnlyList<string> GetDistinctMetricKeys();
}
