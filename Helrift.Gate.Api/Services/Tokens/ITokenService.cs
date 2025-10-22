namespace Helrift.Gate.Api.Services.Tokens
{
    public sealed class TokenIssueRequest
    {
        public string AccountId { get; init; }
        public string SteamId { get; init; }
        public string BuildVersion { get; init; }
    }

    public sealed class RefreshResult
    {
        public bool Success { get; init; }
        public string GateSession { get; init; }
        public string RefreshToken { get; init; }
        public string SteamId { get; init; }
    }

    public interface ITokenService
    {
        Task<(string GateSession, string RefreshToken)> IssueAsync(TokenIssueRequest req);
        Task<RefreshResult> RefreshAsync(string refreshToken);
    }
}
