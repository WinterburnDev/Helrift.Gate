using System.Net.Http;
using System.Text;
using Helrift.Gate.Api.Services.GameServers.Models;
using Helrift.Gate.Api.Services.Routing;
using Newtonsoft.Json;

public sealed class HttpReservationClient : IReservationClient
{
    private readonly HttpClient _http = new();

    private sealed class ReservePayload
    {
        public string accountId { get; set; }
        public string characterId { get; set; }
        public string jti { get; set; }
        public string expiresAtUtc { get; set; }
    }

    public async Task<bool> ReserveAsync(GameServerDescriptor gs, string accountId, string characterId, string jti, DateTimeOffset expiresAt, CancellationToken ct)
    {
        if (gs?.InternalUrl is null) return false;

        var url = $"{gs.InternalUrl.TrimEnd('/')}/internal/sessions/reserve";
        var body = new ReservePayload
        {
            accountId = accountId,
            characterId = characterId,
            jti = jti,
            expiresAtUtc = expiresAt.UtcDateTime.ToString("o")
        };
        var json = JsonConvert.SerializeObject(body);
        var res = await _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"), ct);
        return res.IsSuccessStatusCode;
    }
}