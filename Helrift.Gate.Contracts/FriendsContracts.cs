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
        public List<FriendRequestStatusDto> incomingRequests { get; set; } = new();
        public List<FriendRequestStatusDto> outgoingRequests { get; set; } = new();
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

    public sealed class FriendRequestStatusDto
    {
        public string characterId { get; set; } = string.Empty; // other character id
        public string name { get; set; } = string.Empty;        // other character name
        public string direction { get; set; } = string.Empty;   // "incoming" or "outgoing" (mostly redundant but handy)
    }

    // what we load from Firebase in character doc:
    public class FriendEntry
    {
        public string note { get; set; }
        public string since { get; set; }
        public string name { get; set; } // optional denorm
    }

    public sealed class FriendRequestEntry
    {
        public string? name { get; set; }
        public string? direction { get; set; }
        public string? since { get; set; }
    }

    public sealed class SendFriendRequestDto
    {
        public string TargetName { get; set; } = string.Empty;
    }

}
