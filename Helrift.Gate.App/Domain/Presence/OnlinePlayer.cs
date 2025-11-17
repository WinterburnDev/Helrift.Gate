using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Helrift.Gate.App.Domain
{
    public sealed class OnlinePlayer
    {
        public string CharacterId { get; set; }
        public string CharacterName { get; set; }
        public string GameServerId { get; set; }
        public string Side { get; set; }
        public DateTime LastSeenUtc { get; set; }
    }
}
