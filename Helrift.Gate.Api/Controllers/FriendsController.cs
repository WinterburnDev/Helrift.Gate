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
            //if (!IsAccountOwner(accountId)) return Forbid();

            var chars = await _data.GetCharactersAsync(accountId, ct);
            if (!chars.Any(c => string.Equals(c.Id, characterId, StringComparison.Ordinal)))
                return NotFound();

            var list = await _friends.GetFriendsSnapshotAsync(accountId, characterId, ct);

            var envelope = new Envelope<FriendsSnapshotPayload>(
                "friends.snapshot",
                new FriendsSnapshotPayload { friends = list }
            );

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
    }
}
