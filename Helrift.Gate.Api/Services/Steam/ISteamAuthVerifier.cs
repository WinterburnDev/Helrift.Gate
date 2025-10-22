// Services/Steam/ISteamAuthVerifier.cs
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Helrift.Gate.Api.Services.Steam
{
    public interface ISteamAuthVerifier
    {
        Task<SteamVerifyResult> VerifyAsync(string base64Ticket, string steamId64);
    }

    public sealed class SteamVerifyResult
    {
        public bool Success { get; init; }
        public string SteamId64 { get; init; }
        public string Error { get; init; }
    }
}