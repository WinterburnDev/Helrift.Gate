using Helrift.Gate.App;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/v1/accounts/{accountId}/characters")]
public sealed class CharactersController(IGameDataProvider data) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Character>>> List(string accountId, CancellationToken ct)
        => Ok(await data.GetCharactersAsync(accountId, ct));

    [HttpGet("{charId}")]
    public async Task<ActionResult<Character>> Get(string accountId, string charId, CancellationToken ct)
    {
        var c = await data.GetCharacterAsync(accountId, charId, ct);
        return c is null ? NotFound() : Ok(c);
    }
}
