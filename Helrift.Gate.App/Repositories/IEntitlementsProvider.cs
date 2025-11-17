using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Helrift.Gate.Contracts;

public interface IEntitlementsDataProvider
{
    /// <summary>Reads the current entitlements id from aliases.</summary>
    Task<string?> GetCurrentIdAsync(CancellationToken ct);

    /// <summary>
    /// Reads unlockables for a given entitlements id.
    /// Returns rows keyed by their canonical dotted id.
    /// </summary>
    Task<IReadOnlyDictionary<string, EntitlementUnlockableRow>> GetUnlockablesByIdAsync(string entitlementsId, CancellationToken ct);
}