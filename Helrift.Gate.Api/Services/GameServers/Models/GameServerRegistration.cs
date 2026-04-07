using Helrift.Gate.Contracts;
using System;
using System.Collections.Generic;

namespace Helrift.Gate.Api.Services.GameServers.Models
{
    public sealed class GameServerRegistration
    {
        public string GameServerId { get; set; } = "";
        public string BuildVersion { get; set; } = "";
        public long RegisteredAtUnixUtc { get; set; }
        public RealmConfigDto RealmConfig { get; set; } = new();
        public IReadOnlyList<MapInfoDto> Maps { get; set; } = Array.Empty<MapInfoDto>();
        public GameServerWeatherStateDto? WeatherState { get; set; }
        public DateTime LastHeartbeatUtc { get; set; }
    }
}