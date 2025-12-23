// Controllers/AuthController.cs
using Helrift.Gate.Api.Services.Accounts;
using Helrift.Gate.Api.Services.Steam;
using Helrift.Gate.Api.Services.Tokens;
using Helrift.Gate.Contracts;
using Helrift.Gate.Contracts.Realm;
using Helrift.Gate.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly ISteamAuthVerifier _steam;
    private readonly IAccountService _accounts;
    private readonly ITokenService _tokens;
    private readonly IBanService _bans;
    private readonly IRealmService _realm;

    public AuthController(
        ISteamAuthVerifier steam,
        IAccountService accounts,
        ITokenService tokens,
        IBanService bans,
        IRealmService realm)
    {
        _steam = steam;
        _accounts = accounts;
        _tokens = tokens;
        _bans = bans;
        _realm = realm;
    }

    [HttpPost("steam")]
    public async Task<ActionResult<AuthSteamResponse>> Steam([FromBody] AuthSteamRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.ticket) ||
            string.IsNullOrWhiteSpace(req?.buildVersion) ||
            string.IsNullOrWhiteSpace(req?.steamId))
        {
            return BadRequest(new AuthFailureResponse
            {
                errorCode = (int)AuthFailureReason.InvalidRequest,
                errorKey = AuthFailureReason.InvalidRequest.ToString()
            });
        }

        // =========================================================
        // Guard 1: Maintenance / scheduled shutdown
        // =========================================================
        var realmState = _realm.GetState();
        if (realmState.DenyNewLogins)
            return RealmUnavailable(realmState);

        // =========================================================
        // Verify Steam
        // =========================================================
        var verify = await _steam.VerifyAsync(req.ticket, req.steamId);
        if (!verify.Success)
        {
            return Unauthorized(new AuthFailureResponse
            {
                errorCode = (int)AuthFailureReason.SteamVerificationFailed,
                errorKey = AuthFailureReason.SteamVerificationFailed.ToString()
            });
        }

        // =========================================================
        // Ban check
        // =========================================================
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

        // =========================================================
        // Guard 2: Hard capacity cap (count-only)
        // =========================================================
        // NOTE: this relies on presence being reasonably up to date.
        // If you later need strict gating, add an in-flight counter w/ TTL in IRealmService.
        if (!_realm.IsLoginAllowed())
        {
            var max = _realm.GetMaxPlayers();
            var cur = _realm.GetCurrentPlayers();

            // Standard hint for clients / proxies.
            Response.Headers["Retry-After"] = "30";

            return StatusCode(StatusCodes.Status429TooManyRequests,
                new AuthFailureResponse
                {
                    errorCode = (int)AuthFailureReason.RealmFull,
                    errorKey = AuthFailureReason.RealmFull.ToString(),
                    retryAfterSeconds = 30,
                    realmMessage = $"Realm is full ({cur}/{max}). Please try again soon."
                });
        }

        // =========================================================
        // Account + tokens
        // =========================================================
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
            profile = new AuthSteamProfile
            {
                steamId = verify.SteamId64,
                accountId = account.Id
            }
        });
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthSteamResponse>> Refresh([FromBody] AuthRefreshRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.refreshToken))
        {
            return BadRequest(new AuthFailureResponse
            {
                errorCode = (int)AuthFailureReason.InvalidRequest,
                errorKey = AuthFailureReason.InvalidRequest.ToString()
            });
        }

        // Optional: block refresh during maintenance/shutdown (keeps behaviour consistent)
        var realmState = _realm.GetState();
        if (realmState.DenyNewLogins)
            return RealmUnavailable(realmState);

        var result = await _tokens.RefreshAsync(req.refreshToken);
        if (result == null || !result.Success || string.IsNullOrWhiteSpace(result.GateSession))
        {
            return Unauthorized(new AuthFailureResponse
            {
                errorCode = (int)AuthFailureReason.InvalidSession,
                errorKey = AuthFailureReason.InvalidSession.ToString()
            });
        }

        return Ok(new AuthSteamResponse
        {
            gateSession = result.GateSession,
            refreshToken = result.RefreshToken,
            profile = new AuthSteamProfile
            {
                steamId = result.SteamId,
                accountId = result.AccountId
            }
        });
    }

    private ObjectResult RealmUnavailable(RealmState realmState)
    {
        int? retryAfterSeconds = null;
        long? shutdownAtUnixUtc = null;

        if (realmState.ShutdownAtUtc.HasValue)
        {
            var shutdownAt = realmState.ShutdownAtUtc.Value;
            retryAfterSeconds = RealmTime.SecondsUntil(shutdownAt);
            shutdownAtUnixUtc = shutdownAt.ToUnixTimeSeconds();
        }

        // Standard hint for clients / proxies.
        // If we know an exact countdown, use it; otherwise just a small backoff.
        var headerRetry = retryAfterSeconds.HasValue ? Math.Max(1, retryAfterSeconds.Value).ToString() : "30";
        Response.Headers["Retry-After"] = headerRetry;

        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            new AuthFailureResponse
            {
                errorCode = (int)AuthFailureReason.RealmUnavailable,
                errorKey = AuthFailureReason.RealmUnavailable.ToString(),
                retryAfterSeconds = retryAfterSeconds,
                shutdownAtUnixUtc = shutdownAtUnixUtc,
                realmMessage = realmState.ShutdownAtUtc.HasValue
                    ? $"Server restarting in {retryAfterSeconds}s"
                    : "Server is in maintenance mode"
            });
    }
}
