// Gate/Controllers/FriendsController.cs
using Helrift.Gate.Api.Services.Friends;
using Helrift.Gate.App;
using Helrift.Gate.Contracts;
using Helrift.Gate.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Gate.Controllers
{
    [ApiController]
    [Route("api/accounts/{accountId}/characters/{characterId}/friends")]
    //[Authorize]
    public class FriendsController : ControllerBase
    {
        private readonly IFriendsService _friends;
        private readonly IGameDataProvider _data;

        public FriendsController(IFriendsService friends, IGameDataProvider data)
        {
            _friends = friends;
            _data = data;
        }

        private bool IsAccountOwner(string accountId)
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return string.Equals(sub, accountId, StringComparison.Ordinal);
        }

        [HttpGet("snapshot")]
        //[Authorize]
        public async Task<ActionResult<Envelope<FriendsSnapshotPayload>>> GetSnapshot(
            [FromRoute] string accountId,
            [FromRoute] string characterId,
            CancellationToken ct)
        {
            //if (!IsAccountOwner(accountId) return Forbid();

            var me = await _data.GetCharacterAsync(accountId, characterId, ct);
            if (me == null)
                return NotFound();

            // accepted friends (already mapped via service)
            var friendsList = await _friends.GetFriendsSnapshotAsync(accountId, characterId, ct);

            var incoming = new List<FriendRequestStatusDto>();
            var outgoing = new List<FriendRequestStatusDto>();

            var requests = me.FriendRequests ?? new Dictionary<string, FriendRequestEntry>();
            foreach (var kv in requests)
            {
                var otherCharId = kv.Key;
                var req = kv.Value;
                if (string.IsNullOrEmpty(otherCharId) || req == null)
                    continue;

                var dir = string.IsNullOrWhiteSpace(req.direction)
                    ? "incoming"
                    : req.direction;

                var dto = new FriendRequestStatusDto
                {
                    characterId = otherCharId,
                    name = req.name ?? string.Empty,
                    direction = dir
                };

                if (dir.Equals("incoming", StringComparison.OrdinalIgnoreCase))
                    incoming.Add(dto);
                else
                    outgoing.Add(dto);
            }

            var envelope = new Envelope<FriendsSnapshotPayload>
            {
                type = "friends.snapshot",
                payload = new FriendsSnapshotPayload
                {
                    friends = friendsList,
                    incomingRequests = incoming,
                    outgoingRequests = outgoing
                }
            };

            return Ok(envelope);
        }


        [HttpPost]
        //[Authorize]
        public async Task<ActionResult<FriendStatusDto>> AddFriend(
            [FromRoute] string accountId,
            [FromRoute] string characterId,
            [FromBody] AddFriendRequest request,
            CancellationToken ct)
        {
            //if (!IsAccountOwner(accountId)) return Forbid();

            // dont allow this for now. we might re-add this as an admin tool. but all friend logic must now go through the 2 phase process
            return BadRequest(); 

            // ensure character exists (and belongs to account)
            var me = await _data.GetCharacterAsync(accountId, characterId, ct);
            if (me == null)
                return NotFound();

            var dto = await _friends.AddFriendAsync(
                accountId,
                characterId,
                request.FriendCharacterId,
                request.FriendName,
                ct);

            if (dto == null)
                return BadRequest(); // invalid input / self-friend / etc.

            return Ok(dto);
        }

        [HttpDelete("{friendCharacterId}")]
        //[Authorize]
        public async Task<IActionResult> DeleteFriend(
            [FromRoute] string accountId,
            [FromRoute] string characterId,
            [FromRoute] string friendCharacterId,
            CancellationToken ct)
        {
            if (!IsAccountOwner(accountId)) return Forbid();

            var me = await _data.GetCharacterAsync(accountId, characterId, ct);
            if (me == null)
                return NotFound();

            var removed = await _friends.DeleteFriendAsync(accountId, characterId, friendCharacterId, ct);
            if (!removed)
                return NotFound(); // nothing to delete

            return NoContent();
        }

        [HttpPost("requests")]
        public async Task<IActionResult> SendFriendRequest(
            [FromRoute] string accountId,
            [FromRoute] string characterId,
            [FromBody] SendFriendRequestDto body,
            CancellationToken ct)
        {
            //if (!IsAccountOwner(accountId)) return Forbid();

            if (string.IsNullOrWhiteSpace(body?.TargetName))
                return BadRequest("TargetName is required.");

            var me = await _data.GetCharacterAsync(accountId, characterId, ct);
            if (me == null)
                return NotFound();

            var ok = await _friends.SendFriendRequestAsync(accountId, characterId, body.TargetName, ct);
            if (!ok)
                return BadRequest("Unable to send friend request.");

            return NoContent();
        }

        [HttpPost("requests/{fromCharacterId}/accept")]
        public async Task<IActionResult> AcceptFriendRequest(
            [FromRoute] string accountId,
            [FromRoute] string characterId,
            [FromRoute] string fromCharacterId,
            CancellationToken ct)
        {
            //if (!IsAccountOwner(accountId)) return Forbid();

            var me = await _data.GetCharacterAsync(accountId, characterId, ct);
            if (me == null)
                return NotFound();

            var ok = await _friends.AcceptFriendRequestAsync(accountId, characterId, fromCharacterId, ct);
            if (!ok)
                return BadRequest("Unable to accept friend request.");

            return NoContent();
        }

        [HttpPost("requests/{fromCharacterId}/reject")]
        public async Task<IActionResult> RejectFriendRequest(
            [FromRoute] string accountId,
            [FromRoute] string characterId,
            [FromRoute] string fromCharacterId,
            CancellationToken ct)
        {
            //if (!IsAccountOwner(accountId)) return Forbid();

            var me = await _data.GetCharacterAsync(accountId, characterId, ct);
            if (me == null)
                return NotFound();

            var ok = await _friends.RejectFriendRequestAsync(accountId, characterId, fromCharacterId, ct);
            if (!ok)
                return BadRequest("Unable to reject friend request.");

            return NoContent();
        }

        // POST /api/accounts/{accountId}/characters/{characterId}/friends/requests/{targetCharacterId}/cancel
        [HttpPost("requests/{targetCharacterId}/cancel")]
        public async Task<IActionResult> CancelFriendRequest(
            [FromRoute] string accountId,
            [FromRoute] string characterId,
            [FromRoute] string targetCharacterId,
            CancellationToken ct)
        {
            //if (!IsAccountOwner(accountId)) return Forbid();

            var me = await _data.GetCharacterAsync(accountId, characterId, ct);
            if (me == null)
                return NotFound();

            var ok = await _friends.CancelFriendRequestAsync(accountId, characterId, targetCharacterId, ct);
            if (!ok)
                return BadRequest("Unable to cancel friend request.");

            return NoContent();
        }

    }
}
