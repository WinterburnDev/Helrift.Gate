using Helrift.Gate.Api.Services.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

public sealed class Hs256JoinTokenService : IJoinTokenService
{
    private readonly JwtJoinOptions _opt;
    private readonly SymmetricSecurityKey _key;
    private readonly SigningCredentials _creds;

    public Hs256JoinTokenService(IOptions<JwtJoinOptions> opt)
    {
        _opt = opt.Value;
        var bytes = Encoding.UTF8.GetBytes(_opt.Hs256Secret);
        if (bytes.Length < 32) throw new ArgumentOutOfRangeException(nameof(_opt.Hs256Secret), "HS256 secret must be >= 32 bytes.");
        _key = new SymmetricSecurityKey(bytes);
        _creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
    }

    public string MintJoinToken(string accountId, string characterId, string gsId, string buildVersion, out string jti)
    {
        jti = Guid.NewGuid().ToString("N");

        var now = DateTimeOffset.UtcNow;
        var exp = now.AddMinutes(_opt.JoinMinutes);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, accountId), // your master account id
            new Claim("char",  characterId),
            new Claim("gs",    gsId),
            new Claim("build", buildVersion),
            new Claim(JwtRegisteredClaimNames.Jti, jti),
        };

        var tok = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: exp.UtcDateTime,
            signingCredentials: _creds);

        return new JwtSecurityTokenHandler().WriteToken(tok);
    }
}