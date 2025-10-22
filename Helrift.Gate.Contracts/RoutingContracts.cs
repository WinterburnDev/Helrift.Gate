using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Helrift.Gate.Contracts
{
    public sealed class CharacterRouteRequest
    {
        public string characterId { get; set; }
    }

    public sealed class CharacterRouteResponse
    {
        public string gsId { get; set; }
        public string ip { get; set; }
        public int port { get; set; }
        public string joinToken { get; set; }
        public TransportHints transport { get; set; }
    }

    public sealed class TransportHints
    {
        public string type { get; set; } = "fishnet.tugboat";
        public int[] channels { get; set; } = Array.Empty<int>();
    }
}
