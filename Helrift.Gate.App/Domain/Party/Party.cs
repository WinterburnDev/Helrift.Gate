using Helrift.Gate.Contracts;

namespace Helrift.Gate.App.Domain
{
    public class Party
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string LeaderCharacterId { get; set; } = default!;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public string PartyName { get; set; } = default!;
        public OwnerSide Side { get; set; }
        public PartyExpMode ExpMode { get; set; } = PartyExpMode.EqualShare;
        public PartyVisibility Visibility { get; set; } = PartyVisibility.Public;
        public List<PartyMember> Members { get; set; } = new();
    }
}
