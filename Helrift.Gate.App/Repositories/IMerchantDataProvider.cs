using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Helrift.Gate.App.Repositories;

public interface IMerchantDataProvider
{
    Task<MerchantPageResult> QueryAsync(string npcId, MerchantQuery q, CancellationToken ct);
    Task<MerchantItemRow?> GetAsync(string npcId, string listingId, CancellationToken ct);

    Task<string?> TryInsertAsync(string npcId, MerchantItemRow row, CancellationToken ct);
    Task<bool> TryDeleteAsync(string npcId, string listingId, CancellationToken ct);

    Task<(bool ok, int newQty)> TryDecrementQuantityOrDeleteAsync(string npcId, string listingId, int count, CancellationToken ct);
    Task<bool> TryIncrementQuantityAsync(string npcId, string listingId, int delta, CancellationToken ct);
    Task<(bool merged, string? listingId)> TryMergeStackableAsync(string npcId, MerchantItemRow row, CancellationToken ct);

    Task<int> DeleteExpiredAsync(string npcId, long nowUnix, int maxBatch, CancellationToken ct);
    Task<int> TrimOverflowAsync(string npcId, int maxItems, long nowUnix, CancellationToken ct);
    Task<IReadOnlyList<MerchantItemRow>> GetAllForMergeAsync(string npcId, long nowUnix, CancellationToken ct);
}
