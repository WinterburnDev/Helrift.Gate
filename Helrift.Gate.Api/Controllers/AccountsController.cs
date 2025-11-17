using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/v1/accounts")]
public sealed class AccountsController(IGameDataProvider data) : ControllerBase
{
    [HttpGet("{accountId}")]
    public async Task<ActionResult<AccountData>> Get(string accountId, CancellationToken ct)
        => (await data.GetAccountAsync(accountId, ct)) is { } acc ? Ok(acc) : NotFound();
}
