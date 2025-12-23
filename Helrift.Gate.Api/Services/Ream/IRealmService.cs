using Helrift.Gate.Contracts;
using Helrift.Gate.Contracts.Realm;

namespace Helrift.Gate.Services;

public interface IRealmService
{
    // Queries
    RealmState GetState();
    bool IsLoginAllowed();

    // Operations
    RealmOperation ScheduleShutdown(TimeSpan inTime, string message, string initiatedBy);

    RealmOperation EnableMaintenance(string message, string initiatedBy);

    void ClearAllOperations();
    void ClearOperation(Guid operationId);

    int GetMaxPlayers();
    int GetCurrentPlayers();
}
