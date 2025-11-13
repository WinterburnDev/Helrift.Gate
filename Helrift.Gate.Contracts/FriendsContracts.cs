using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Helrift.Gate.Contracts
{
    public class FriendsSnapshotPayload
    {
        public List<FriendStatusDto> friends { get; set; }
    }

    public class AddFriendRequest
    {
        public string FriendCharacterId { get; set; } = string.Empty;
        public string? FriendName { get; set; }
    }

    public class FriendStatusDto
    {
        public string characterId { get; set; }
        public string name { get; set; }      // optional, if we have it
        public bool online { get; set; }
        public string server { get; set; }    // optional
    }

    // what we load from Firebase in character doc:
    public class FriendEntry
    {
        public string note { get; set; }
        public string since { get; set; }
        public string name { get; set; } // optional denorm
    }
}
