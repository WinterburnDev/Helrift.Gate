using Helrift.Gate.Api.Services.GameServers.Models;
using Helrift.Gate.Api.Services.GameServers;
using Microsoft.Extensions.Options;
using System.Linq;

public sealed class ConfigGameServerDirectory : IGameServerDirectory
{
    private readonly Dictionary<string, GameServerDescriptor> _byId;

    public ConfigGameServerDirectory(IOptions<GameServersOptions> opt)
    {
        _byId = (opt.Value?.Servers ?? new()).ToDictionary(s => s.Id, StringComparer.Ordinal);
    }

    public GameServerDescriptor GetById(string gsId)
        => gsId != null && _byId.TryGetValue(gsId, out var s) ? s : null;

    public IReadOnlyList<GameServerDescriptor> All() => _byId.Values.ToList();

    public GameServerDescriptor PickForCharacter(string mapIdOrShard)
    {
        if (mapIdOrShard.Equals("adminzone"))
        {
            var ret = _byId.Values.FirstOrDefault(s => s.Id == "gs-02");
            if (ret != null)
                return ret;
        }

        // TODO: replace with map->server lookup. For now: first server.
        return _byId.Values.FirstOrDefault();
    }
}