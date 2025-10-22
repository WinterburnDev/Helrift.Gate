using System.IdentityModel.Tokens.Jwt;
using System.Management;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Helrift.Gate.Api.Services.Tokens
{
    public sealed class JwtTokenService : ITokenService
    {
        private readonly JwtOptions _opt;
        private readonly IRefreshTokenStore _store;

        public JwtTokenService(IOptions<JwtOptions> opt, IRefreshTokenStore store)
        { _opt = opt.Value; _store = store; }

        public async Task<(string GateSession, string RefreshToken)> IssueAsync(TokenIssueRequest req)
        {
            var now = DateTimeOffset.UtcNow;

            var claims = new[]
            {
            new Claim(JwtRegisteredClaimNames.Sub, req.AccountId),
            new Claim("steamid", req.SteamId),
            new Claim("build", req.BuildVersion),
            new Claim("scope", "gate"),
            new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Hs256Secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var jwt = new JwtSecurityToken(
                issuer: _opt.Issuer,
                audience: _opt.Audience,
                claims: claims,
                notBefore: now.UtcDateTime,
                expires: now.AddMinutes(_opt.AccessMinutes).UtcDateTime,
                signingCredentials: creds
            );
            var token = new JwtSecurityTokenHandler().WriteToken(jwt);

            var refresh = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + "." + Guid.NewGuid().ToString("N");
            await _store.SaveAsync(refresh, req.AccountId, req.SteamId, req.BuildVersion,
                now.AddDays(_opt.RefreshDays));

            return (token, refresh);
        }

        public async Task<RefreshResult> RefreshAsync(string refreshToken)
        {
            var record = await _store.GetAsync(refreshToken);
            if (record == null || record.ExpiresUtc <= DateTimeOffset.UtcNow)
                return new RefreshResult { Success = false };

            var (jwt, newRefresh) = await IssueAsync(new TokenIssueRequest
            {
                AccountId = record.MasterClientId,
                SteamId = record.SteamId,
                BuildVersion = record.BuildVersion
            });

            // Optionally revoke old refresh and store new; for dev we can keep both
            return new RefreshResult { Success = true, GateSession = jwt, RefreshToken = newRefresh, SteamId = record.SteamId };
        }
    }
}
