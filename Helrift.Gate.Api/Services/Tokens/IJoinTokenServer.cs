namespace Helrift.Gate.Api.Services.Tokens
{
    public interface IJoinTokenService
    {
        string MintJoinToken(string accountId, string characterId, string gsId, string buildVersion, out string jti);
    }
}
