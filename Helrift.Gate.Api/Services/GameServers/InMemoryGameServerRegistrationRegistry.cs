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
                LastHeartbeatUtc = DateTime.UtcNow
            };

            _registrations[registration.GameServerId] = registration;
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