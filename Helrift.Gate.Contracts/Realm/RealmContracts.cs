using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Helrift.Gate.Contracts.Realm
{
    public sealed record RealmOperation(
    Guid Id,
    RealmOperationType Type,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset? EndsAtUtc,   // null => until cleared
    string Message,
    string InitiatedBy
);

    public sealed record RealmState(
        bool DenyNewLogins,
        bool DenyNewJoins,
        DateTimeOffset? ShutdownAtUtc,
        IReadOnlyList<RealmOperation> ActiveOperations
    );

    public static class RealmTime
    {
        public static int SecondsUntil(DateTimeOffset atUtc) =>
            Math.Max(0, (int)Math.Ceiling((atUtc - DateTimeOffset.UtcNow).TotalSeconds));
    }

    public sealed class RealmStateDto
    {
        public bool denyNewLogins { get; set; }
        public bool denyNewJoins { get; set; }
        public long? shutdownAtUnixUtc { get; set; }
        public string? realmMessage { get; set; }
    }

}
