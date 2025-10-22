using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Helrift.Gate.Api.Services.Auth
{
    public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
    {
        private readonly IConfiguration _cfg;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<ApiKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            IConfiguration cfg)
            : base(options, logger, encoder, clock) => _cfg = cfg;

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var headerName = _cfg["ServerApi:Header"] ?? "X-GameServer-Key";
            var expected = _cfg["ServerApi:Key"];

            if (string.IsNullOrWhiteSpace(expected))
                return Task.FromResult(AuthenticateResult.Fail("Server API key not configured."));

            if (!Request.Headers.TryGetValue(headerName, out var provided))
                return Task.FromResult(AuthenticateResult.NoResult());

            if (!string.Equals(provided.ToString(), expected, StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

            var id = new ClaimsIdentity(ApiKeyAuthenticationOptions.DefaultScheme);
            id.AddClaim(new Claim(ClaimTypes.Role, "server"));

            var principal = new ClaimsPrincipal(id);
            var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationOptions.DefaultScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}