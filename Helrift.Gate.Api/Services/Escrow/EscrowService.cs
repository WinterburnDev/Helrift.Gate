using System.Collections.Concurrent;
using System.Text.Json;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.Escrow;

public sealed class EscrowService : IEscrowService
{
    private const string OpStatusStarted = "started";
    private const string OpStatusApplying = "applying";
    private const string OpStatusCompleted = "completed";
    private const string OpStatusNeedsReconciliation = "needs_reconciliation";

    private readonly IEscrowRepository _repo;
    private readonly IGameDataProvider _game;
    private readonly IEscrowBalanceService _balances;
    private readonly ILogger<EscrowService> _logger;
    private readonly IGameServerEscrowBridge _bridge;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    public EscrowService(
        IEscrowRepository repo,
        IGameDataProvider game,
        IEscrowBalanceService balances,
        ILogger<EscrowService> logger,
        IGameServerEscrowBridge bridge)
    {
        _repo = repo;
        _game = game;
        _balances = balances;
        _logger = logger;
        _bridge = bridge;
    }

    public IReadOnlyList<EscrowCapability> GetSupportMatrix()
        => _balances.GetCapabilities();

    public async Task<EscrowIntegrityReport> GetIntegrityReportAsync(string realmId, string containerId, CancellationToken ct = default)
    {
        var report = new EscrowIntegrityReport
        {
            RealmId = realmId,
            ContainerId = containerId,
            GeneratedUtc = DateTime.UtcNow
        };

        var container = await _repo.GetContainerAsync(realmId, containerId, ct);
        if (container == null)
        {
            report.Healthy = false;
            report.Issues.Add(new EscrowIntegrityIssue
            {
                Severity = "error",
                Code = "container_not_found",
                Message = "Escrow container was not found."
            });
            return report;
        }

        foreach (var op in container.Operations.Where(o => !string.Equals(o.Status, OpStatusCompleted, StringComparison.OrdinalIgnoreCase)))
        {
            report.Healthy = false;
            report.Issues.Add(new EscrowIntegrityIssue
            {
                Severity = "warning",
                Code = "operation_incomplete",
                Message = $"Operation '{op.Operation}' has non-completed status '{op.Status}'.",
                OperationIdempotencyKey = op.IdempotencyKey
            });
        }

        foreach (var asset in container.Assets)
        {
            if (asset.AssetType == EscrowAssetType.ItemInstance &&
                asset.State is EscrowAssetState.Escrowed or EscrowAssetState.Claimable or EscrowAssetState.Claimed or EscrowAssetState.Returned &&
                asset.ItemInstancePayload == null)
            {
                report.Healthy = false;
                report.Issues.Add(new EscrowIntegrityIssue
                {
                    Severity = "error",
                    Code = "item_payload_missing",
                    Message = "Item asset is in a transferred/resolved state but payload is missing.",
                    AssetId = asset.Id
                });
            }
        }

        return report;
    }

    public async Task<EscrowContainer> CreateEscrowContainerAsync(CreateEscrowContainerRequest request, CancellationToken ct = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var realmId = string.IsNullOrWhiteSpace(request.RealmId) ? "default" : request.RealmId;
        var containerId = string.IsNullOrWhiteSpace(request.ContainerId) ? Guid.NewGuid().ToString("N") : request.ContainerId!;

        // Best-effort duplicate suppression when caller supplies container id.
        var existing = await _repo.GetContainerAsync(realmId, containerId, ct);
        if (existing != null) return existing;

        var now = DateTime.UtcNow;
        var container = new EscrowContainer
        {
            Id = containerId,
            RealmId = realmId,
            EscrowType = request.EscrowType,
            SourceFeature = request.SourceFeature,
            SourceEntityType = request.SourceEntityType,
            SourceEntityId = request.SourceEntityId,
            State = EscrowContainerState.Created,
            CreatedUtc = now,
            UpdatedUtc = now,
            ExpiresUtc = request.ExpiresUtc,
            ResolutionPolicy = request.ResolutionPolicy,
            Metadata = request.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Participants = request.Participants ?? [],
            Version = 1
        };

        await _repo.CreateContainerAsync(container, ct);
        return container;
    }

    public Task<EscrowContainer?> GetEscrowContainerAsync(string realmId, string containerId, CancellationToken ct = default)
        => _repo.GetContainerAsync(realmId, containerId, ct);

    public async Task<EscrowSummary?> GetEscrowSummaryAsync(string realmId, string containerId, CancellationToken ct = default)
    {
        var c = await _repo.GetContainerAsync(realmId, containerId, ct);
        if (c == null) return null;

        return new EscrowSummary
        {
            ContainerId = c.Id,
            RealmId = c.RealmId,
            State = c.State,
            TotalAssets = c.Assets.Count,
            ClaimableAssets = c.Assets.Count(a => a.State == EscrowAssetState.Claimable),
            ClaimedAssets = c.Assets.Count(a => a.State == EscrowAssetState.Claimed),
            ReturnedAssets = c.Assets.Count(a => a.State == EscrowAssetState.Returned),
            ExpiredAssets = c.Assets.Count(a => a.State == EscrowAssetState.Expired),
            UpdatedUtc = c.UpdatedUtc
        };
    }

    public Task<EscrowContainer> AddAssetsAsync(AddEscrowAssetsRequest request, CancellationToken ct = default)
        => MutateAsync(request.RealmId, request.ContainerId, request.IdempotencyKey, "add_assets", request.ActorType, request.ActorId, async c =>
        {
            var now = DateTime.UtcNow;
            foreach (var draft in request.Assets ?? [])
            {
                var asset = new EscrowAsset
                {
                    Id = Guid.NewGuid().ToString("N"),
                    AssetType = draft.AssetType,
                    HoldingMode = draft.HoldingMode,

                    // IMPORTANT:
                    // Start all assets as Reserved. EscrowAssetsAsync is responsible for moving
                    // item payloads / debiting balances and then setting Escrowed.
                    State = EscrowAssetState.Reserved,

                    SubtypeKey = draft.SubtypeKey ?? string.Empty,
                    QuantityValue = draft.QuantityValue,
                    SourceParticipantId = draft.SourceParticipantId,
                    BeneficiaryParticipantId = draft.BeneficiaryParticipantId,
                    SourceAccountId = draft.SourceAccountId,
                    SourceCharacterId = draft.SourceCharacterId,
                    SourceInventory = string.IsNullOrWhiteSpace(draft.SourceInventory) ? "inventory" : draft.SourceInventory,
                    RecipientAccountId = draft.RecipientAccountId,
                    RecipientCharacterId = draft.RecipientCharacterId,
                    RecipientInventory = string.IsNullOrWhiteSpace(draft.RecipientInventory) ? "inventory" : draft.RecipientInventory,
                    ItemInstanceId = draft.ItemInstanceId,
                    BalanceKey = draft.BalanceKey,
                    Metadata = draft.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal)
                };

                c.Assets.Add(asset);
                c.AuditEntries.Add(NewAudit(c.Id, asset, "asset.added", request.ActorType, request.ActorId, now));
            }

            c.State = c.Assets.Count == 0 ? EscrowContainerState.Created : EscrowContainerState.Pending;
            await Task.CompletedTask;
        }, ct);

    public Task<EscrowContainer> EscrowAssetsAsync(EscrowActionRequest request, CancellationToken ct = default)
        => MutateAsync(request.RealmId, request.ContainerId, request.IdempotencyKey, "escrow_assets", request.ActorType, request.ActorId, async c =>
        {
            var now = DateTime.UtcNow;
            var assets = SelectAssets(c, request.AssetIds);

            foreach (var asset in assets)
            {
                if (asset.State is EscrowAssetState.Escrowed or EscrowAssetState.Claimable or EscrowAssetState.Claimed)
                    continue;

                await MoveIntoEscrowAsync(asset, $"{request.IdempotencyKey}:escrow_assets:{asset.Id}", ct);
                asset.State = EscrowAssetState.Escrowed;

                c.AuditEntries.Add(NewAudit(c.Id, asset, "asset.escrowed", request.ActorType, request.ActorId, now));
            }

            c.State = EscrowContainerState.Active;
        }, ct);

    public Task<EscrowContainer> MakeAssetsClaimableAsync(EscrowActionRequest request, CancellationToken ct = default)
        => MutateAsync(request.RealmId, request.ContainerId, request.IdempotencyKey, "make_claimable", request.ActorType, request.ActorId, async c =>
        {
            var now = DateTime.UtcNow;
            foreach (var asset in SelectAssets(c, request.AssetIds))
            {
                if (asset.State == EscrowAssetState.Claimed || asset.State == EscrowAssetState.Returned)
                    continue;

                if (asset.State is EscrowAssetState.Escrowed or EscrowAssetState.Reserved)
                {
                    asset.State = EscrowAssetState.Claimable;
                    asset.ClaimableUtc = now;
                    c.AuditEntries.Add(NewAudit(c.Id, asset, "asset.claimable", request.ActorType, request.ActorId, now));
                }
            }

            await Task.CompletedTask;
        }, ct);

    public Task<EscrowContainer> ClaimAssetsAsync(EscrowActionRequest request, CancellationToken ct = default)
        => ReleaseInternalAsync(request, markClaimed: true, operationName: "claim_assets", ct);

    public Task<EscrowContainer> ReleaseAssetsAsync(EscrowActionRequest request, CancellationToken ct = default)
        => ReleaseInternalAsync(request, markClaimed: true, operationName: "release_assets", ct);

    public Task<EscrowContainer> ReturnAssetsAsync(EscrowActionRequest request, CancellationToken ct = default)
        => MutateAsync(request.RealmId, request.ContainerId, request.IdempotencyKey, "return_assets", request.ActorType, request.ActorId, async c =>
        {
            var now = DateTime.UtcNow;
            foreach (var asset in SelectAssets(c, request.AssetIds))
            {
                if (asset.State is EscrowAssetState.Returned or EscrowAssetState.Claimed)
                    continue;

                await ApplyAssetToTargetAsync(
                    asset,
                    request.TargetAccountId ?? asset.SourceAccountId,
                    request.TargetCharacterId ?? asset.SourceCharacterId,
                    request.TargetInventory ?? asset.SourceInventory ?? "inventory",
                    $"{request.IdempotencyKey}:return_assets:{asset.Id}",
                    ct);

                asset.State = EscrowAssetState.Returned;
                asset.ReturnedUtc = now;
                c.AuditEntries.Add(NewAudit(c.Id, asset, "asset.returned", request.ActorType, request.ActorId, now));
            }

            if (c.Assets.All(a => a.State is EscrowAssetState.Returned or EscrowAssetState.Claimed or EscrowAssetState.Forfeited or EscrowAssetState.Expired))
                c.State = EscrowContainerState.Resolved;
        }, ct);

    public Task<EscrowContainer> ExpireEscrowAsync(EscrowActionRequest request, CancellationToken ct = default)
        => MutateAsync(request.RealmId, request.ContainerId, request.IdempotencyKey, "expire_escrow", request.ActorType, request.ActorId, async c =>
        {
            var now = DateTime.UtcNow;
            foreach (var asset in SelectAssets(c, request.AssetIds))
            {
                if (asset.State is EscrowAssetState.Claimed or EscrowAssetState.Returned or EscrowAssetState.Forfeited)
                    continue;

                asset.State = EscrowAssetState.Expired;
                asset.ExpiredUtc = now;
                c.AuditEntries.Add(NewAudit(c.Id, asset, "asset.expired", request.ActorType, request.ActorId, now));
            }

            c.State = EscrowContainerState.Expired;
            await Task.CompletedTask;
        }, ct);

    public Task<EscrowContainer> CancelEscrowAsync(EscrowActionRequest request, CancellationToken ct = default)
        => MutateAsync(request.RealmId, request.ContainerId, request.IdempotencyKey, "cancel_escrow", request.ActorType, request.ActorId, async c =>
        {
            c.State = EscrowContainerState.Cancelled;
            c.AuditEntries.Add(new EscrowAuditEntry
            {
                ContainerId = c.Id,
                Action = "container.cancelled",
                ActorType = request.ActorType,
                ActorId = request.ActorId,
                Utc = DateTime.UtcNow
            });
            await Task.CompletedTask;
        }, ct);

    private Task<EscrowContainer> ReleaseInternalAsync(EscrowActionRequest request, bool markClaimed, string operationName, CancellationToken ct)
        => MutateAsync(request.RealmId, request.ContainerId, request.IdempotencyKey, operationName, request.ActorType, request.ActorId, async c =>
        {
            var now = DateTime.UtcNow;
            foreach (var asset in SelectAssets(c, request.AssetIds))
            {
                if (asset.State is EscrowAssetState.Claimed or EscrowAssetState.Returned)
                    continue;

                await ApplyAssetToTargetAsync(
                    asset,
                    request.TargetAccountId ?? asset.RecipientAccountId,
                    request.TargetCharacterId ?? asset.RecipientCharacterId,
                    request.TargetInventory ?? asset.RecipientInventory ?? "inventory",
                    $"{request.IdempotencyKey}:{operationName}:{asset.Id}",
                    ct);

                if (markClaimed)
                {
                    asset.State = EscrowAssetState.Claimed;
                    asset.ClaimedUtc = now;
                }

                c.AuditEntries.Add(NewAudit(c.Id, asset, "asset.released", request.ActorType, request.ActorId, now));
            }

            if (c.Assets.All(a => a.State is EscrowAssetState.Claimed or EscrowAssetState.Returned or EscrowAssetState.Forfeited or EscrowAssetState.Expired))
                c.State = EscrowContainerState.Resolved;
        }, ct);

    private async Task MoveIntoEscrowAsync(EscrowAsset asset, string mutationKey, CancellationToken ct)
    {
        if (asset.AssetType == EscrowAssetType.ItemInstance)
        {
            if (string.IsNullOrWhiteSpace(asset.SourceAccountId) || string.IsNullOrWhiteSpace(asset.SourceCharacterId))
                throw new InvalidOperationException("Item escrow requires source account/character.");

            if (_bridge.TryGetOnlineServerId(asset.SourceCharacterId ?? string.Empty, out _))
            {
                var payload = await _bridge.RequestMoveFromSourceAsync(new GameServerEscrowMoveFromSourceRequest
                {
                    OperationId = mutationKey,
                    RealmId = "default",
                    SourceAccountId = asset.SourceAccountId ?? string.Empty,
                    SourceCharacterId = asset.SourceCharacterId ?? string.Empty,
                    SourceInventory = asset.SourceInventory ?? "inventory",
                    ItemInstanceId = asset.ItemInstanceId ?? string.Empty,
                    Quantity = Math.Max(1, asset.QuantityValue)
                }, ct);

                asset.ItemInstancePayload = payload;
                asset.ItemInstanceId = payload.UniqueId;
                asset.QuantityValue = Math.Max(1, payload.Quantity);
                return;
            }

            var character = await _game.GetCharacterAsync(asset.SourceAccountId, asset.SourceCharacterId, ct)
                ?? throw new InvalidOperationException("Source character not found.");

            var bag = ReadBag(character, asset.SourceInventory ?? "inventory");
            var idx = bag.FindIndex(x => string.Equals(x.UniqueId, asset.ItemInstanceId, StringComparison.Ordinal));

            if (idx < 0)
                throw new InvalidOperationException($"Item {asset.ItemInstanceId} was not found in source inventory.");

            var sourceItem = bag[idx];
            var requestedQty = Math.Max(1, asset.QuantityValue);
            var sourceQty = Math.Max(1, sourceItem.Quantity);

            if (requestedQty > sourceQty)
                throw new InvalidOperationException($"Requested quantity {requestedQty} exceeds source stack quantity {sourceQty}.");

            CharacterItemData escrowedItem;
            if (requestedQty == sourceQty)
            {
                // Full stack transfer.
                bag.RemoveAt(idx);
                escrowedItem = CloneItem(sourceItem);
            }
            else
            {
                // Split stack transfer (gold and other stackables).
                sourceItem.Quantity = checked((int)(sourceQty - requestedQty));
                bag[idx] = sourceItem;

                escrowedItem = CloneItem(sourceItem);
                escrowedItem.UniqueId = Guid.NewGuid().ToString("N");
                escrowedItem.Quantity = checked((int)requestedQty);
                escrowedItem.IsEquipped = false;
                escrowedItem.EquipmentSlot = EquipmentSlot.None;
                escrowedItem.BagPosition = default;
            }

            asset.ItemInstancePayload ??= escrowedItem;
            asset.ItemInstanceId ??= escrowedItem.UniqueId;
            asset.QuantityValue = escrowedItem.Quantity;

            WriteBag(character, asset.SourceInventory ?? "inventory", bag);
            await _game.SaveCharacterAsync(character, ct);
            return;
        }

        if (asset.AssetType is EscrowAssetType.Currency or EscrowAssetType.PointBalance)
        {
            if (string.IsNullOrWhiteSpace(asset.SourceAccountId) || string.IsNullOrWhiteSpace(asset.SourceCharacterId))
                throw new InvalidOperationException("Balance escrow requires source account/character.");
            if (string.IsNullOrWhiteSpace(asset.BalanceKey))
                throw new InvalidOperationException("BalanceKey is required for balance escrow.");
            if (!_balances.IsSupported(asset.BalanceKey))
                throw new InvalidOperationException($"Balance '{asset.BalanceKey}' is not supported by escrow.");

            await _balances.EnsureDebitAsync(new EscrowBalanceMutationRequest(
                asset.SourceAccountId,
                asset.SourceCharacterId,
                asset.BalanceKey,
                asset.QuantityValue,
                mutationKey), ct);
        }
    }

    private async Task ApplyAssetToTargetAsync(
        EscrowAsset asset,
        string? targetAccountId,
        string? targetCharacterId,
        string targetInventory,
        string mutationKey,
        CancellationToken ct)
    {
        if (asset.AssetType == EscrowAssetType.ItemInstance)
        {
            if (asset.ItemInstancePayload == null)
                throw new InvalidOperationException("Missing escrowed item payload.");
            if (string.IsNullOrWhiteSpace(targetAccountId) || string.IsNullOrWhiteSpace(targetCharacterId))
                throw new InvalidOperationException("Target account/character is required for item release.");

            if (_bridge.TryGetOnlineServerId(targetCharacterId ?? string.Empty, out _))
            {
                await _bridge.RequestGrantToTargetAsync(new GameServerEscrowGrantToTargetRequest
                {
                    OperationId = mutationKey,
                    RealmId = "default",
                    TargetAccountId = targetAccountId ?? string.Empty,
                    TargetCharacterId = targetCharacterId ?? string.Empty,
                    TargetInventory = targetInventory,
                    ItemPayload = CloneItem(asset.ItemInstancePayload!)
                }, ct);

                return;
            }

            var target = await _game.GetCharacterAsync(targetAccountId, targetCharacterId, ct)
                ?? throw new InvalidOperationException("Target character not found.");

            var bag = ReadBag(target, targetInventory);
            if (!bag.Any(x => string.Equals(x.UniqueId, asset.ItemInstancePayload.UniqueId, StringComparison.Ordinal)))
                bag.Add(CloneItem(asset.ItemInstancePayload));

            WriteBag(target, targetInventory, bag);
            await _game.SaveCharacterAsync(target, ct);
            return;
        }

        if (asset.AssetType is EscrowAssetType.Currency or EscrowAssetType.PointBalance)
        {
            if (string.IsNullOrWhiteSpace(asset.BalanceKey))
                throw new InvalidOperationException("BalanceKey is required for balance release.");
            if (string.IsNullOrWhiteSpace(targetAccountId) || string.IsNullOrWhiteSpace(targetCharacterId))
                throw new InvalidOperationException("Target account/character is required for balance release.");
            if (!_balances.IsSupported(asset.BalanceKey))
                throw new InvalidOperationException($"Balance '{asset.BalanceKey}' is not supported by escrow.");

            await _balances.EnsureCreditAsync(new EscrowBalanceMutationRequest(
                targetAccountId,
                targetCharacterId,
                asset.BalanceKey,
                asset.QuantityValue,
                mutationKey), ct);
        }
    }

    private async Task<EscrowContainer> MutateAsync(
        string realmId,
        string containerId,
        string idempotencyKey,
        string operation,
        string actorType,
        string actorId,
        Func<EscrowContainer, Task> mutate,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));

        var lockKey = $"{realmId}:{containerId}";
        using var _ = await AcquireLockAsync(lockKey, ct);

        const int stageAttempts = 5;
        EscrowContainerSnapshot? stagedSnapshot = null;
        EscrowContainer? stagedContainer = null;

        for (var attempt = 1; attempt <= stageAttempts; attempt++)
        {
            var snap = await _repo.GetContainerSnapshotAsync(realmId, containerId, ct)
                ?? throw new InvalidOperationException("Escrow container not found.");

            var container = snap.Container;
            var op = UpsertOperation(container, idempotencyKey, operation, actorType, actorId);

            if (string.Equals(op.Status, OpStatusCompleted, StringComparison.OrdinalIgnoreCase))
                return container;

            if (string.Equals(op.Status, OpStatusApplying, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Operation '{idempotencyKey}' is already applying.");

            op.Status = OpStatusApplying;
            op.Error = null;
            op.CompletedUtc = null;
            if (op.StartedUtc == default) op.StartedUtc = DateTime.UtcNow;

            container.UpdatedUtc = DateTime.UtcNow;
            container.Version++;

            var staged = await _repo.TryReplaceContainerAsync(realmId, container, snap.ConcurrencyToken, ct);
            if (!staged) continue;

            stagedSnapshot = await _repo.GetContainerSnapshotAsync(realmId, containerId, ct);
            stagedContainer = stagedSnapshot?.Container;
            break;
        }

        if (stagedSnapshot == null || stagedContainer == null)
            throw new InvalidOperationException("Unable to stage escrow operation due to concurrent updates.");

        var working = stagedContainer;
        var workingOp = UpsertOperation(working, idempotencyKey, operation, actorType, actorId);

        try
        {
            await mutate(working);

            workingOp.Status = OpStatusCompleted;
            workingOp.CompletedUtc = DateTime.UtcNow;
            workingOp.Error = null;

            working.UpdatedUtc = DateTime.UtcNow;
            working.Version++;

            var finalized = await _repo.TryReplaceContainerAsync(realmId, working, stagedSnapshot.ConcurrencyToken, ct);
            if (finalized) return working;

            await TryMarkOperationNeedsReconciliationAsync(realmId, containerId, idempotencyKey, "finalize_conflict", ct);
            throw new InvalidOperationException("Escrow side-effects applied but finalize write conflicted. Marked needs_reconciliation.");
        }
        catch (Exception ex)
        {
            await TryMarkOperationNeedsReconciliationAsync(realmId, containerId, idempotencyKey, ex.Message, ct);
            throw;
        }
    }

    private static EscrowOperation UpsertOperation(EscrowContainer container, string idempotencyKey, string operation, string actorType, string actorId)
    {
        var op = container.Operations.LastOrDefault(o => string.Equals(o.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
        if (op != null) return op;

        op = new EscrowOperation
        {
            IdempotencyKey = idempotencyKey,
            Operation = operation,
            ActorType = actorType,
            ActorId = actorId,
            StartedUtc = DateTime.UtcNow,
            Status = OpStatusStarted
        };

        container.Operations.Add(op);
        return op;
    }

    private async Task TryMarkOperationNeedsReconciliationAsync(
        string realmId,
        string containerId,
        string idempotencyKey,
        string error,
        CancellationToken ct)
    {
        try
        {
            var snap = await _repo.GetContainerSnapshotAsync(realmId, containerId, ct);
            if (snap == null) return;

            var c = snap.Container;
            var op = c.Operations.LastOrDefault(o => string.Equals(o.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
            if (op == null) return;

            if (string.Equals(op.Status, OpStatusCompleted, StringComparison.OrdinalIgnoreCase))
                return;

            op.Status = OpStatusNeedsReconciliation;
            op.Error = error;

            c.UpdatedUtc = DateTime.UtcNow;
            c.Version++;
            await _repo.TryReplaceContainerAsync(realmId, c, snap.ConcurrencyToken, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark escrow operation as needs_reconciliation. realm={RealmId} container={ContainerId} op={OpKey}", realmId, containerId, idempotencyKey);
        }
    }

    private static List<EscrowAsset> SelectAssets(EscrowContainer c, IReadOnlyList<string>? assetIds)
    {
        if (assetIds == null || assetIds.Count == 0)
            return c.Assets;

        var set = new HashSet<string>(assetIds, StringComparer.Ordinal);
        return c.Assets.Where(a => set.Contains(a.Id)).ToList();
    }

    private static EscrowAuditEntry NewAudit(string containerId, EscrowAsset asset, string action, string actorType, string actorId, DateTime utc)
        => new()
        {
            ContainerId = containerId,
            AssetId = asset.Id,
            Action = action,
            SourceParticipantId = asset.SourceParticipantId,
            BeneficiaryParticipantId = asset.BeneficiaryParticipantId,
            ActorType = actorType,
            ActorId = actorId,
            Utc = utc,
            ItemInstanceId = asset.ItemInstanceId,
            BalanceKey = asset.BalanceKey,
            BalanceAmount = asset.AssetType is EscrowAssetType.Currency or EscrowAssetType.PointBalance ? asset.QuantityValue : null,
            MetadataJson = asset.Metadata.Count == 0 ? null : JsonSerializer.Serialize(asset.Metadata)
        };

    private static List<CharacterItemData> ReadBag(CharacterData c, string bag)
    {
        return string.Equals(bag, "warehouse", StringComparison.OrdinalIgnoreCase)
            ? (c.Warehouse ?? Array.Empty<CharacterItemData>()).ToList()
            : (c.Inventory ?? Array.Empty<CharacterItemData>()).ToList();
    }

    private static void WriteBag(CharacterData c, string bag, List<CharacterItemData> items)
    {
        if (string.Equals(bag, "warehouse", StringComparison.OrdinalIgnoreCase))
            c.Warehouse = items.ToArray();
        else
            c.Inventory = items.ToArray();
    }

    private static CharacterItemData CloneItem(CharacterItemData item)
    {
        var json = JsonSerializer.Serialize(item);
        return JsonSerializer.Deserialize<CharacterItemData>(json)!;
    }

    private static async Task<IDisposable> AcquireLockAsync(string key, CancellationToken ct)
    {
        var sem = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        return new Releaser(sem);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _sem;
        public Releaser(SemaphoreSlim sem) => _sem = sem;
        public void Dispose() => _sem.Release();
    }
}