using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Helrift.Gate.Contracts
{
    [Serializable]
    public sealed class EntitlementGrants
    {
        public List<string> ItemSkinIds { get; init; } = new();
        public List<string> CastEffectIds { get; init; } = new();
        public List<string> HairStyleIds { get; init; } = new();
        public List<string> HairColorIds { get; init; } = new();
        public List<string> FeatureFlags { get; init; } = new();
    }

    [Serializable]
    public sealed class EntitlementUnlockableRow
    {
        /// <summary>Canonical dotted id, e.g. "unlock.skin.offhand.orb.fire".</summary>
        public string Id { get; init; } = "";
        public string Type { get; init; } = "cosmetic";
        public string Scope { get; init; } = "account";
        public bool Active { get; init; } = true;
        public int Version { get; init; } = 1;
        public EntitlementGrants Grants { get; init; } = new();
    }
}
