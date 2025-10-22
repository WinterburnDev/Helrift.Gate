using Helrift.Gate.Api.Services.GameServers.Models;

namespace Helrift.Gate.Api.Services.GameServers
{
    public sealed class GameServersOptions
    {
        public List<GameServerDescriptor> Servers { get; set; } = new();
    }

}
