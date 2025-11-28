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
    private readonly IBanService _bans;

    public AuthController(ISteamAuthVerifier steam, IAccountService accounts, ITokenService tokens, IBanService bans)
    {
        _steam = steam;
        _accounts = accounts;
        _tokens = tokens;
        _bans = bans;
    }

    [HttpPost("steam")]
    public async Task<ActionResult<AuthSteamResponse>> Steam([FromBody] AuthSteamRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.ticket) ||
            string.IsNullOrWhiteSpace(req?.buildVersion) ||
            string.IsNullOrWhiteSpace(req?.steamId))
                return BadRequest(new AuthFailureResponse
                {
                    errorCode = (int)AuthFailureReason.InvalidRequest,
                    errorKey = AuthFailureReason.InvalidRequest.ToString()
                });

        var verify = await _steam.VerifyAsync(req.ticket, req.steamId);
        if (!verify.Success)
        {
            return Unauthorized(new AuthFailureResponse
            {
                errorCode = (int)AuthFailureReason.SteamVerificationFailed,
                errorKey = AuthFailureReason.SteamVerificationFailed.ToString()
            });
        }

        /*if (req.buildVersion != GateConfig.CurrentBuild)
        {
            return StatusCode(StatusCodes.Status426UpgradeRequired,
                new AuthFailureResponse
                {
                    errorCode = (int)AuthFailureReason.VersionMismatch,
                    errorKey = AuthFailureReason.VersionMismatch.ToString()
                });
        }*/

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        var ban = await _bans.GetActiveBanAsync("default", verify.SteamId64, ip);
        if (ban != null)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new AuthFailureResponse
                {
                    errorCode = (int)AuthFailureReason.Banned,
                    errorKey = AuthFailureReason.Banned.ToString(),
                    bannedAtUnixUtc = ban.BannedAtUnixUtc,
                    bannedUntilUnixUtc = ban.ExpiresAtUnixUtc,
                    steamId = verify.SteamId64,
                    reason = ban.Reason
                });
        }

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

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthSteamResponse>> Refresh([FromBody] AuthRefreshRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.refreshToken))
        {
            return BadRequest(new AuthFailureResponse
            {
                errorCode = (int)AuthFailureReason.InvalidRequest,
                errorKey = AuthFailureReason.InvalidRequest.ToString()
            });
        }

        var result = await _tokens.RefreshAsync(req.refreshToken);
        if (result == null || !result.Success || string.IsNullOrWhiteSpace(result.GateSession))
        {
            // Invalid / expired / revoked refresh token
            return Unauthorized(new AuthFailureResponse
            {
                errorCode = (int)AuthFailureReason.InvalidSession,
                errorKey = AuthFailureReason.InvalidSession.ToString()
            });
        }

        var response = new AuthSteamResponse
        {
            gateSession = result.GateSession,
            refreshToken = result.RefreshToken,
            profile = new AuthSteamProfile
            {
                steamId = result.SteamId,
                accountId = result.AccountId
            }
        };

        return Ok(response);
    }

}
