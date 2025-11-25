using Helrift.Gate.Api.Services.Accounts;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers.Admin;

[ApiController]
[Route("admin/api/[controller]")]
public class CharactersController : ControllerBase
{
    private readonly ICharacterSearchService _search;

    public CharactersController(ICharacterSearchService search)
    {
        _search = search;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Query parameter 'name' is required.");

        var results = await _search.SearchByNameAsync(name, ct);
        return Ok(results);
    }
}