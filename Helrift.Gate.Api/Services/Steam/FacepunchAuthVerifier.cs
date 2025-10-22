using Steamworks;

namespace Helrift.Gate.Api.Services.Steam
{
    public sealed class FacepunchAuthVerifier : ISteamAuthVerifier
    {
        public Task<SteamVerifyResult> VerifyAsync(string base64Ticket, string steamId64)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(base64Ticket) || string.IsNullOrWhiteSpace(steamId64))
                    return Task.FromResult(new SteamVerifyResult { Success = false, Error = "Missing ticket or steamId" });

                if (!ulong.TryParse(steamId64, out var sid))
                    return Task.FromResult(new SteamVerifyResult { Success = false, Error = "Invalid steamId" });

                var ticket = Convert.FromBase64String(base64Ticket);
                var result = SteamServer.BeginAuthSession(ticket, (SteamId)sid);

                if (result)
                {
                    // Gate only verifies; we can end the session immediately.
                    SteamServer.EndSession((SteamId)sid);
                    return Task.FromResult(new SteamVerifyResult { Success = true, SteamId64 = steamId64 });
                }

                return Task.FromResult(new SteamVerifyResult
                {
                    Success = false,
                    Error = $"BeginAuthSession failed: {result}"
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new SteamVerifyResult { Success = false, Error = ex.Message });
            }
        }
    }
}
