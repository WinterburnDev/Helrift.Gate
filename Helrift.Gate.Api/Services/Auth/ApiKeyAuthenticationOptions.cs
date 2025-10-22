using Microsoft.AspNetCore.Authentication;

namespace Helrift.Gate.Api.Services.Auth
{
    public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
        public const string DefaultScheme = "ApiKey";
        public string Scheme => DefaultScheme;
        public string AuthenticationType = DefaultScheme;
    }
}
