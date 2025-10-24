// Controllers/AuthController.cs
using Helrift.Gate.Api.Services.Accounts;
using Helrift.Gate.Api.Services.Steam;
using Helrift.Gate.Api.Services.Tokens;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly ISteamAuthVerifier _steam;
    private readonly IAccountService _accounts;
    private readonly ITokenService _tokens;

    public AuthController(ISteamAuthVerifier steam, IAccountService accounts, ITokenService tokens)
    {
        _steam = steam;
        _accounts = accounts;
        _tokens = tokens;
    }

    [HttpPost("steam")]
    public async Task<ActionResult<AuthSteamResponse>> Steam([FromBody] AuthSteamRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.ticket) ||
            string.IsNullOrWhiteSpace(req?.buildVersion) ||
            string.IsNullOrWhiteSpace(req?.steamId))
            return BadRequest("Missing ticket, buildVersion, or steamId.");

        var verify = await _steam.VerifyAsync(req.ticket, req.steamId);
        if (!verify.Success) return Unauthorized(verify.Error);

        var account = await _accounts.GetOrCreateBySteamAsync(verify.SteamId64);

        var (jwt, refresh) = await _tokens.IssueAsync(new TokenIssueRequest
        {
            AccountId = account.Id,
            SteamId = verify.SteamId64,
            BuildVersion = req.buildVersion
        });

        return Ok(new AuthSteamResponse
        {
            gateSession = jwt,
            refreshToken = refresh,
            profile = new AuthSteamProfile() { steamId = verify.SteamId64, accountId = account.Id }
        });
    }
}
