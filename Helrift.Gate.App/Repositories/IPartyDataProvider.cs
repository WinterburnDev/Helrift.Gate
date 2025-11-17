using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Helrift.Gate.App.Domain;

namespace Helrift.Gate.App.Repositories
{
    public interface IPartyDataProvider
    {
        Task<Party?> GetByIdAsync(string partyId, CancellationToken ct);
        Task<Party?> GetByCharacterIdAsync(string characterId, CancellationToken ct);
        Task SaveAsync(Party party, CancellationToken ct);
        Task DeleteAsync(string partyId, CancellationToken ct);
        Task<IReadOnlyCollection<Party>> GetAllAsync(CancellationToken ct);
    }

}
