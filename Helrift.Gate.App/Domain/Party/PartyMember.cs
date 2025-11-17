using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Helrift.Gate.App.Domain
{
    public class PartyMember
    {
        public string CharacterId { get; set; } = default!;
        public string AccountId { get; set; } = default!;
        public string CharacterName { get; set; } = default!;

        // these are dynamic and can be refreshed via presence
        public string? CurrentServerId { get; set; }
        public bool IsOnline { get; set; }
    }
}
