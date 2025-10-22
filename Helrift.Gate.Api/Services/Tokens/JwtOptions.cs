namespace Helrift.Gate.Api.Services.Tokens
{
    public sealed class JwtOptions
    {
        public string Issuer { get; set; } = "gate";
        public string Audience { get; set; } = "gate";
        public string Hs256Secret { get; set; }          // dev secret
        public int AccessMinutes { get; set; } = 15;
        public int RefreshDays { get; set; } = 30;
    }

    public sealed class JwtJoinOptions
    {
        public string Issuer { get; set; } = "gate";
        public string Audience { get; set; } = "gameserver";
        public string Hs256Secret { get; set; } = string.Empty;
        public int JoinMinutes { get; set; } = 2;
    }
}
