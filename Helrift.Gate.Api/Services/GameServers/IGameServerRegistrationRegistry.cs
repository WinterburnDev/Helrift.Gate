using Helrift.Gate.Api.Services.GameServers.Models;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.GameServers
{
    public interface IGameServerRegistrationRegistry
    {
        GameServerRegistration Upsert(GameServerRegistrationDto dto);
        GameServerRegistration? Get(string gameServerId);
        IReadOnlyCollection<GameServerRegistration> GetAll();
    }
}