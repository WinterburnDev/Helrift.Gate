using Helrift.Gate.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Helrift.Gate.App
{
    public interface IGuildDataProvider
    {
        Task<GuildData?> GetAsync(string guildId, CancellationToken ct);
        Task<bool> SaveAsync(GuildData guild, CancellationToken ct);
        Task<bool> DeleteAsync(string guildId, CancellationToken ct);
        Task<IReadOnlyList<GuildData>> QueryAsync(string? side, string? partialName, CancellationToken ct);
    }
}
