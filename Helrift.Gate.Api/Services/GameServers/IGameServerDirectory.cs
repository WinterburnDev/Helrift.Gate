using Helrift.Gate.Api.Services.GameServers.Models;

namespace Helrift.Gate.Api.Services.GameServers
{
    public interface IGameServerDirectory
    {
        GameServerDescriptor GetById(string gsId);
        IReadOnlyList<GameServerDescriptor> All();
        GameServerDescriptor PickForCharacter(string mapIdOrShard); // simple strategy for now
    }
}
