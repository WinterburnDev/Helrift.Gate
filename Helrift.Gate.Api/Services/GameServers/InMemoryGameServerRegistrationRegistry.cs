using Helrift.Gate.Api.Services.GameServers.Models;
using Helrift.Gate.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Helrift.Gate.Api.Services.GameServers
{
    public sealed class InMemoryGameServerRegistrationRegistry : IGameServerRegistrationRegistry
    {
        private readonly ConcurrentDictionary<string, GameServerRegistration> _registrations =
            new(StringComparer.Ordinal);

        public GameServerRegistration Upsert(GameServerRegistrationDto dto)
        {
            var maps = dto.maps?.ToList() ?? new List<MapInfoDto>();
            var realmConfig = dto.realmConfig ?? new RealmConfigDto();

            var registration = new GameServerRegistration
            {
                GameServerId = dto.gameServerId ?? "",
                BuildVersion = dto.buildVersion ?? "",
                RegisteredAtUnixUtc = dto.registeredAtUnixUtc,
                RealmConfig = realmConfig,
                Maps = maps,
                WeatherState = dto.weatherState,
                LastHeartbeatUtc = DateTime.UtcNow
            };

            _registrations[registration.GameServerId] = registration;
            return registration;
        }

        public GameServerRegistration UpsertWeatherState(GameServerWeatherStateDto dto)
        {
            var gameServerId = dto.gameServerId ?? string.Empty;

            var registration = _registrations.AddOrUpdate(
                gameServerId,
                _ => new GameServerRegistration
                {
                    GameServerId = gameServerId,
                    WeatherState = dto,
                    LastHeartbeatUtc = DateTime.UtcNow
                },
                (_, existing) =>
                {
                    existing.WeatherState = dto;
                    existing.LastHeartbeatUtc = DateTime.UtcNow;
                    return existing;
                });

            return registration;
        }

        public GameServerRegistration? Get(string gameServerId)
        {
            return _registrations.TryGetValue(gameServerId, out var registration)
                ? registration
                : null;
        }

        public IReadOnlyCollection<GameServerRegistration> GetAll()
        {
            return _registrations.Values.ToList();
        }
    }
}