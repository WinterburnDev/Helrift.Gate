using Helrift.Gate.Contracts;
using Helrift.Gate.Contracts.Realm;
using Helrift.Gate.App.Domain; // OnlinePlayer
using System;
using System.Collections.Generic;
using System.Linq;

namespace Helrift.Gate.Services;

public sealed class RealmService : IRealmService
{
    private readonly object _lock = new();
    private readonly List<RealmOperation> _operations = new();

    // hard-coded for now
    private const int MaxPlayers = 200;

    // count-only (driven by PresenceService transitions)
    private int _currentPlayers;

    public RealmService(IPresenceService presence)
    {
        if (presence == null) throw new ArgumentNullException(nameof(presence));

        // Initialise from snapshot (count-only)
        _currentPlayers = presence.GetAll().Count;

        // Track transitions (IMPORTANT: PresenceService should invoke events outside its internal lock)
        presence.PlayerCameOnline += OnPlayerCameOnline;
        presence.PlayerWentOffline += OnPlayerWentOffline;
    }

    private void OnPlayerCameOnline(OnlinePlayer _)
    {
        lock (_lock)
        {
            _currentPlayers++;
        }
    }

    private void OnPlayerWentOffline(OnlinePlayer _)
    {
        lock (_lock)
        {
            _currentPlayers = Math.Max(0, _currentPlayers - 1);
        }
    }

    // ---------------------------
    // Queries
    // ---------------------------

    public RealmState GetState()
    {
        lock (_lock)
        {
            var ops = _operations.ToArray();

            var shutdown = ops.FirstOrDefault(o => o.Type == RealmOperationType.Shutdown);
            var maintenance = ops.Any(o => o.Type == RealmOperationType.MaintenanceMode);

            return new RealmState(
                DenyNewLogins: maintenance || shutdown != null,
                DenyNewJoins: maintenance || shutdown != null,
                ShutdownAtUtc: shutdown?.EndsAtUtc,
                ActiveOperations: ops
            );
        }
    }

    public bool IsLoginAllowed()
    {
        var state = GetState();
        if (state.DenyNewLogins)
            return false;

        lock (_lock)
            return _currentPlayers < MaxPlayers;
    }

    // ---------------------------
    // Operations
    // ---------------------------

    public RealmOperation ScheduleShutdown(TimeSpan inTime, string message, string initiatedBy)
    {
        var now = DateTimeOffset.UtcNow;

        var op = new RealmOperation(
            Id: Guid.NewGuid(),
            Type: RealmOperationType.Shutdown,
            StartsAtUtc: now,
            EndsAtUtc: now.Add(inTime),
            Message: message,
            InitiatedBy: initiatedBy
        );

        lock (_lock)
        {
            _operations.RemoveAll(o => o.Type == RealmOperationType.Shutdown);
            _operations.Add(op);
        }

        return op;
    }

    public RealmOperation EnableMaintenance(string message, string initiatedBy)
    {
        var op = new RealmOperation(
            Id: Guid.NewGuid(),
            Type: RealmOperationType.MaintenanceMode,
            StartsAtUtc: DateTimeOffset.UtcNow,
            EndsAtUtc: null,
            Message: message,
            InitiatedBy: initiatedBy
        );

        lock (_lock)
            _operations.Add(op);

        return op;
    }

    public void ClearAllOperations()
    {
        lock (_lock)
            _operations.Clear();
    }

    public void ClearOperation(Guid operationId)
    {
        lock (_lock)
            _operations.RemoveAll(o => o.Id == operationId);
    }

    // ---------------------------
    // Capacity helpers (kept for your controller/UI)
    // ---------------------------

    public int GetMaxPlayers() => MaxPlayers;

    public int GetCurrentPlayers()
    {
        lock (_lock) return _currentPlayers;
    }
}
