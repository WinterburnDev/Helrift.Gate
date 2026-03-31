using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Helrift.Gate.Api.Services.Escrow;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.Deliveries;

public sealed class DeliveryService : IDeliveryService
{
    private readonly IDeliveryRepository _repo;
    private readonly IEscrowService _escrow;
    private readonly IEscrowRepository _escrowRepo;
    private readonly ILogger<DeliveryService> _logger;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    public DeliveryService(
        IDeliveryRepository repo,
        IEscrowService escrow,
        IEscrowRepository escrowRepo,
        ILogger<DeliveryService> logger)
    {
        _repo = repo;
        _escrow = escrow;
        _escrowRepo = escrowRepo;
        _logger = logger;
    }

    public async Task<DeliveryRecord> CreatePlayerMessageAsync(CreatePlayerMessageRequest request, CancellationToken ct = default)
    {
        ValidateIdempotency(request.IdempotencyKey);

        var realmId = NormalizeRealm(request.RealmId);
        var deliveryId = MakeStableId("msg", request.IdempotencyKey, request.RecipientCharacterId);

        var existing = await _repo.GetAsync(realmId, deliveryId, ct);
        if (existing != null) return existing;

        var now = DateTime.UtcNow;
        var record = new DeliveryRecord
        {
            Id = deliveryId,
            ThreadId = deliveryId,
            RealmId = realmId,
            IdempotencyKey = request.IdempotencyKey,
            Type = DeliveryType.PlayerMessage,
            Channel = DeliveryChannel.Personal,
            State = DeliveryState.Delivered,
            Sender = new DeliverySenderRef
            {
                Type = DeliverySenderType.Character,
                Id = request.SenderCharacterId,
                DisplayName = request.SenderDisplayName
            },
            Recipient = new DeliveryRecipientRef
            {
                Type = DeliveryTargetType.Character,
                Id = request.RecipientCharacterId
            },
            Subject = request.Subject ?? string.Empty,
            Body = request.Body ?? string.Empty,
            IsRead = false,
            HasUnclaimedEscrowAssets = false,
            CreatedUtc = now,
            UpdatedUtc = now,
            ExpiresUtc = request.ExpiresUtc,
            Version = 1
        };

        record.StateHistory.Add(NewTransition(DeliveryState.Delivered, "system", "delivery-service", "Created player message"));

        var ok = await _repo.TryCreateAsync(record, ct);
        if (ok) return record;

        return await _repo.GetAsync(realmId, deliveryId, ct)
            ?? throw new InvalidOperationException("Failed to create player message.");
    }

    public async Task<DeliveryRecord> CreateParcelDeliveryAsync(CreateParcelDeliveryRequest request, CancellationToken ct = default)
    {
        ValidateIdempotency(request.IdempotencyKey);

        if (request.Attachments == null || request.Attachments.Count == 0)
            throw new InvalidOperationException("Parcel requires at least one item attachment.");

        var realmId = NormalizeRealm(request.RealmId);
        var deliveryId = MakeStableId("parcel", request.IdempotencyKey, request.RecipientCharacterId);
        var escrowId = $"escrow_delivery_{deliveryId}";

        using var _ = await AcquireLockAsync($"{realmId}:{deliveryId}", ct);

        var existing = await _repo.GetAsync(realmId, deliveryId, ct);
        if (existing != null) return existing;

        var now = DateTime.UtcNow;
        var delivery = new DeliveryRecord
        {
            Id = deliveryId,
            ThreadId = deliveryId,
            RealmId = realmId,
            IdempotencyKey = request.IdempotencyKey,
            Type = DeliveryType.Parcel,
            Channel = DeliveryChannel.Personal,
            State = DeliveryState.PendingValidation,
            Sender = new DeliverySenderRef
            {
                Type = DeliverySenderType.Character,
                Id = request.SenderCharacterId,
                DisplayName = request.SenderDisplayName
            },
            Recipient = new DeliveryRecipientRef
            {
                Type = DeliveryTargetType.Character,
                Id = request.RecipientCharacterId
            },
            Subject = request.Subject ?? string.Empty,
            Body = request.Body ?? string.Empty,
            EscrowContainerId = escrowId,
            IsRead = false,
            HasUnclaimedEscrowAssets = true,
            CreatedUtc = now,
            UpdatedUtc = now,
            ExpiresUtc = request.ExpiresUtc,
            ReturnToSenderOnExpiry = request.ReturnToSenderOnExpiry,
            Version = 1
        };

        delivery.StateHistory.Add(NewTransition(DeliveryState.PendingValidation, "system", "delivery-service", "Created pending parcel"));

        if (!await _repo.TryCreateAsync(delivery, ct))
        {
            return await _repo.GetAsync(realmId, deliveryId, ct)
                ?? throw new InvalidOperationException("Failed to create parcel delivery.");
        }

        try
        {
            await _escrow.CreateEscrowContainerAsync(new CreateEscrowContainerRequest
            {
                RealmId = realmId,
                ContainerId = escrowId,
                EscrowType = "parcel",
                SourceFeature = "delivery",
                SourceEntityType = "delivery",
                SourceEntityId = deliveryId,
                ExpiresUtc = request.ExpiresUtc,
                ResolutionPolicy = request.ReturnToSenderOnExpiry ? EscrowResolutionPolicy.ReturnToSource : EscrowResolutionPolicy.Manual,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["deliveryId"] = deliveryId,
                    ["deliveryType"] = DeliveryType.Parcel.ToString()
                }
            }, ct);

            var drafts = request.Attachments.Select(a => new EscrowAssetDraft
            {
                AssetType = EscrowAssetType.ItemInstance,
                HoldingMode = EscrowHoldingMode.Escrowed,
                SubtypeKey = a.ItemId ?? string.Empty,
                QuantityValue = Math.Max(1, a.Quantity),
                ItemInstanceId = a.ItemInstanceId,
                SourceAccountId = request.SenderAccountId,
                SourceCharacterId = request.SenderCharacterId,
                SourceInventory = request.SourceInventory,
                RecipientAccountId = request.RecipientAccountId,
                RecipientCharacterId = request.RecipientCharacterId,
                RecipientInventory = request.RecipientInventory,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["deliveryId"] = deliveryId
                }
            }).ToList();

            await _escrow.AddAssetsAsync(new AddEscrowAssetsRequest
            {
                RealmId = realmId,
                ContainerId = escrowId,
                IdempotencyKey = $"{request.IdempotencyKey}:add-assets",
                ActorType = "delivery",
                ActorId = deliveryId,
                Assets = drafts
            }, ct);

            await _escrow.EscrowAssetsAsync(new EscrowActionRequest
            {
                RealmId = realmId,
                ContainerId = escrowId,
                IdempotencyKey = $"{request.IdempotencyKey}:escrow",
                ActorType = "delivery",
                ActorId = deliveryId
            }, ct);

            await _escrow.MakeAssetsClaimableAsync(new EscrowActionRequest
            {
                RealmId = realmId,
                ContainerId = escrowId,
                IdempotencyKey = $"{request.IdempotencyKey}:claimable",
                ActorType = "delivery",
                ActorId = deliveryId
            }, ct);

            await MutateDeliveryAsync(realmId, deliveryId, $"{request.IdempotencyKey}:accept", r =>
            {
                r.State = DeliveryState.Delivered;
                r.UpdatedUtc = DateTime.UtcNow;
                r.HasUnclaimedEscrowAssets = true;
                r.Attachments = drafts.Select(d => new DeliveryAttachmentRef
                {
                    EscrowAssetId = string.Empty, // Asset IDs are discoverable via escrow detail
                    ItemInstanceId = d.ItemInstanceId ?? string.Empty,
                    ItemId = d.SubtypeKey,
                    Quantity = d.QuantityValue
                }).ToList();

                r.StateHistory.Add(NewTransition(DeliveryState.Delivered, "delivery", deliveryId, "Parcel accepted and escrow-linked"));
            }, ct);

            return await _repo.GetAsync(realmId, deliveryId, ct)
                ?? throw new InvalidOperationException("Parcel delivery created but could not be reloaded.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Parcel creation failed. realm={RealmId} delivery={DeliveryId}", realmId, deliveryId);

            await TryMutateDeliveryAsync(realmId, deliveryId, $"{request.IdempotencyKey}:fail", r =>
            {
                r.State = DeliveryState.Failed;
                r.UpdatedUtc = DateTime.UtcNow;
                r.StateHistory.Add(NewTransition(DeliveryState.Failed, "delivery", deliveryId, ex.Message));
            }, ct);

            throw;
        }
    }

    public async Task<IReadOnlyList<DeliveryRecord>> CreateSystemDeliveryAsync(CreateSystemDeliveryRequest request, CancellationToken ct = default)
    {
        ValidateIdempotency(request.IdempotencyKey);

        var recipients = ResolveSystemRecipients(request);
        var results = new List<DeliveryRecord>(recipients.Count);

        for (var i = 0; i < recipients.Count; i++)
        {
            var rec = recipients[i];
            var perKey = $"{request.IdempotencyKey}:sys:{rec.RecipientCharacterId}:{i}";

            DeliveryRecord created;
            if (request.Attachments == null || request.Attachments.Count == 0)
            {
                created = await CreatePlayerMessageAsync(new CreatePlayerMessageRequest
                {
                    RealmId = request.RealmId,
                    IdempotencyKey = perKey,
                    SenderCharacterId = request.SenderId,
                    SenderDisplayName = request.SenderDisplayName,
                    RecipientCharacterId = rec.RecipientCharacterId,
                    Subject = request.Subject,
                    Body = request.Body,
                    ExpiresUtc = request.ExpiresUtc
                }, ct);

                await TryMutateDeliveryAsync(created.RealmId, created.Id, $"{perKey}:set-system", r =>
                {
                    r.Type = DeliveryType.SystemDelivery;
                    r.Channel = DeliveryChannel.System;
                    r.Sender.Type = DeliverySenderType.System;
                    r.Metadata["createdByActorType"] = request.CreatedByActorType;
                    r.Metadata["createdByActorId"] = request.CreatedByActorId;
                    r.UpdatedUtc = DateTime.UtcNow;
                }, ct);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.SourceAccountId) || string.IsNullOrWhiteSpace(request.SourceCharacterId))
                    throw new InvalidOperationException("System item delivery requires SourceAccountId and SourceCharacterId.");

                created = await CreateParcelDeliveryAsync(new CreateParcelDeliveryRequest
                {
                    RealmId = request.RealmId,
                    IdempotencyKey = perKey,
                    SenderAccountId = request.SourceAccountId,
                    SenderCharacterId = request.SourceCharacterId,
                    SenderDisplayName = request.SenderDisplayName,
                    SourceInventory = request.SourceInventory,
                    RecipientAccountId = rec.RecipientAccountId,
                    RecipientCharacterId = rec.RecipientCharacterId,
                    RecipientDisplayName = rec.RecipientCharacterId,
                    RecipientInventory = rec.RecipientInventory,
                    Subject = request.Subject,
                    Body = request.Body,
                    ReturnToSenderOnExpiry = request.ReturnToSenderOnExpiry,
                    ExpiresUtc = request.ExpiresUtc,
                    ActorType = request.CreatedByActorType,
                    ActorId = request.CreatedByActorId,
                    Attachments = request.Attachments
                }, ct);

                await TryMutateDeliveryAsync(created.RealmId, created.Id, $"{perKey}:set-system-parcel", r =>
                {
                    r.Type = DeliveryType.SystemDelivery;
                    r.Channel = DeliveryChannel.Rewards;
                    r.Sender.Type = DeliverySenderType.System;
                    r.Sender.Id = request.SenderId;
                    r.Sender.DisplayName = request.SenderDisplayName;
                    r.Metadata["createdByActorType"] = request.CreatedByActorType;
                    r.Metadata["createdByActorId"] = request.CreatedByActorId;
                    r.UpdatedUtc = DateTime.UtcNow;
                }, ct);
            }

            var reloaded = await _repo.GetAsync(created.RealmId, created.Id, ct) ?? created;
            results.Add(reloaded);
        }

        return results;
    }

    public async Task<IReadOnlyList<DeliveryRecord>> CreateGuildBroadcastAsync(CreateGuildBroadcastRequest request, CancellationToken ct = default)
    {
        ValidateIdempotency(request.IdempotencyKey);

        var recipientIds = request.RecipientCharacterIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var results = new List<DeliveryRecord>(recipientIds.Count);
        foreach (var recipient in recipientIds)
        {
            var perRecipientKey = $"{request.IdempotencyKey}:guild:{request.GuildId}:{recipient}";
            var id = MakeStableId("guild", perRecipientKey, recipient);

            var existing = await _repo.GetAsync(request.RealmId, id, ct);
            if (existing != null)
            {
                results.Add(existing);
                continue;
            }

            var now = DateTime.UtcNow;
            var record = new DeliveryRecord
            {
                Id = id,
                ThreadId = MakeStableId("guild-thread", request.IdempotencyKey, request.GuildId),
                RealmId = NormalizeRealm(request.RealmId),
                IdempotencyKey = perRecipientKey,
                Type = DeliveryType.GuildBroadcast,
                Channel = DeliveryChannel.Guild,
                State = DeliveryState.Delivered,
                Sender = new DeliverySenderRef
                {
                    Type = DeliverySenderType.Guild,
                    Id = request.SenderId,
                    DisplayName = request.SenderDisplayName
                },
                Recipient = new DeliveryRecipientRef
                {
                    Type = DeliveryTargetType.Character,
                    Id = recipient
                },
                Subject = request.Subject ?? string.Empty,
                Body = request.Body ?? string.Empty,
                IsRead = false,
                HasUnclaimedEscrowAssets = false,
                CreatedUtc = now,
                UpdatedUtc = now,
                ExpiresUtc = request.ExpiresUtc,
                Version = 1
            };

            record.StateHistory.Add(NewTransition(DeliveryState.Delivered, "guild", request.GuildId, "Guild broadcast fanout"));

            var ok = await _repo.TryCreateAsync(record, ct);
            if (!ok)
            {
                var reload = await _repo.GetAsync(request.RealmId, id, ct);
                if (reload != null) results.Add(reload);
                continue;
            }

            results.Add(record);
        }

        return results;
    }

    public async Task<DeliveryInboxSummary> GetInboxSummaryAsync(string realmId, string recipientCharacterId, CancellationToken ct = default)
    {
        var entries = await _repo.ListRecipientEntriesAsync(realmId, recipientCharacterId, ct);
        var now = DateTime.UtcNow;

        return new DeliveryInboxSummary
        {
            RealmId = NormalizeRealm(realmId),
            RecipientCharacterId = recipientCharacterId,
            TotalCount = entries.Count,
            UnreadCount = entries.Count(x => !x.IsRead && !x.IsArchived),
            UnclaimedEscrowCount = entries.Count(x => x.HasUnclaimedEscrowAssets && !x.IsArchived),
            ExpiredCount = entries.Count(x => x.ExpiresUtc.HasValue && x.ExpiresUtc.Value <= now && !x.IsArchived)
        };
    }

    public async Task<DeliveryNotificationSummary> GetNotificationSummaryAsync(string realmId, string recipientCharacterId, CancellationToken ct = default)
    {
        var entries = await _repo.ListRecipientEntriesAsync(realmId, recipientCharacterId, ct);

        var summary = new DeliveryNotificationSummary
        {
            RealmId = NormalizeRealm(realmId),
            RecipientCharacterId = recipientCharacterId,
            UnreadCount = entries.Count(x => !x.IsRead && !x.IsArchived),
            UnclaimedEscrowCount = entries.Count(x => x.HasUnclaimedEscrowAssets && !x.IsArchived)
        };

        foreach (var g in entries
                     .Where(x => !x.IsArchived)
                     .GroupBy(x => x.Channel.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            summary.ChannelBreakdown[g.Key] = g.Count();
        }

        summary.ShouldNotifyHud = summary.UnreadCount > 0 || summary.UnclaimedEscrowCount > 0;
        return summary;
    }

    public async Task<IReadOnlyList<DeliveryRecord>> GetInboxAsync(string realmId, string recipientCharacterId, DeliveryListQuery query, CancellationToken ct = default)
    {
        query ??= new DeliveryListQuery();

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var entries = await _repo.ListRecipientEntriesAsync(realmId, recipientCharacterId, ct);

        if (!query.IncludeArchived)
            entries = entries.Where(x => !x.IsArchived).ToList();

        var pageEntries = entries
            .OrderByDescending(x => x.UpdatedUnixUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var list = new List<DeliveryRecord>(pageEntries.Count);
        foreach (var e in pageEntries)
        {
            var d = await _repo.GetAsync(realmId, e.DeliveryId, ct);
            if (d != null) list.Add(d);
        }

        return list;
    }

    public async Task<DeliveryRecord?> GetDeliveryAsync(string realmId, string recipientCharacterId, string deliveryId, CancellationToken ct = default)
    {
        var d = await _repo.GetAsync(realmId, deliveryId, ct);
        if (d == null) return null;
        if (!string.Equals(d.Recipient.Id, recipientCharacterId, StringComparison.Ordinal))
            return null;
        return d;
    }

    public async Task<DeliveryRecord> MarkReadAsync(MarkDeliveryReadRequest request, CancellationToken ct = default)
    {
        ValidateIdempotency(request.IdempotencyKey);

        return await MutateDeliveryAsync(request.RealmId, request.DeliveryId, request.IdempotencyKey, r =>
        {
            if (!string.Equals(r.Recipient.Id, request.RecipientCharacterId, StringComparison.Ordinal))
                throw new InvalidOperationException("Recipient mismatch.");

            if (r.IsRead) return;

            r.IsRead = true;
            r.ReadUtc = DateTime.UtcNow;
            r.UpdatedUtc = DateTime.UtcNow;

            if (r.State == DeliveryState.Delivered)
            {
                r.State = DeliveryState.Read;
                r.StateHistory.Add(NewTransition(DeliveryState.Read, "recipient", request.RecipientCharacterId, "Marked as read"));
            }
        }, ct);
    }

    public async Task<DeliveryRecord> ClaimAssetsAsync(ClaimDeliveryAssetsRequest request, CancellationToken ct = default)
    {
        ValidateIdempotency(request.IdempotencyKey);

        var delivery = await _repo.GetAsync(request.RealmId, request.DeliveryId, ct)
            ?? throw new InvalidOperationException("Delivery not found.");

        if (!string.Equals(delivery.Recipient.Id, request.RecipientCharacterId, StringComparison.Ordinal))
            throw new InvalidOperationException("Recipient mismatch.");

        if (string.IsNullOrWhiteSpace(delivery.EscrowContainerId))
            throw new InvalidOperationException("Delivery has no escrow-backed assets.");

        await _escrow.ClaimAssetsAsync(new EscrowActionRequest
        {
            RealmId = delivery.RealmId,
            ContainerId = delivery.EscrowContainerId,
            IdempotencyKey = $"{request.IdempotencyKey}:escrow-claim",
            ActorType = "delivery",
            ActorId = delivery.Id,
            AssetIds = request.EscrowAssetIds,
            TargetAccountId = request.RecipientAccountId,
            TargetCharacterId = request.RecipientCharacterId,
            TargetInventory = request.RecipientInventory
        }, ct);

        var escrowSummary = await _escrow.GetEscrowSummaryAsync(delivery.RealmId, delivery.EscrowContainerId, ct)
            ?? throw new InvalidOperationException("Escrow summary unavailable after claim.");

        return await MutateDeliveryAsync(delivery.RealmId, delivery.Id, request.IdempotencyKey, r =>
        {
            var resolved = escrowSummary.ClaimedAssets + escrowSummary.ReturnedAssets + escrowSummary.ExpiredAssets >= escrowSummary.TotalAssets;
            var hasUnclaimed = escrowSummary.ClaimableAssets > 0 || !resolved;

            r.IsRead = true;
            r.ReadUtc ??= DateTime.UtcNow;
            r.ClaimedUtc = DateTime.UtcNow;
            r.HasUnclaimedEscrowAssets = hasUnclaimed;
            r.State = hasUnclaimed ? DeliveryState.ClaimedPartial : DeliveryState.ClaimedComplete;
            r.UpdatedUtc = DateTime.UtcNow;

            r.StateHistory.Add(NewTransition(r.State, "recipient", request.RecipientCharacterId,
                hasUnclaimed ? "Claimed partial assets." : "Claimed all assets."));
        }, ct);
    }

    public async Task<bool> DeleteAdminAsync(
        string realmId,
        string deliveryId,
        bool cleanupEscrow = true,
        bool force = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deliveryId))
            throw new ArgumentException("deliveryId is required.", nameof(deliveryId));

        var normalizedRealm = NormalizeRealm(realmId);
        var delivery = await _repo.GetAsync(normalizedRealm, deliveryId, ct);
        if (delivery == null) return false;

        if (!string.IsNullOrWhiteSpace(delivery.EscrowContainerId))
        {
            var summary = await _escrow.GetEscrowSummaryAsync(normalizedRealm, delivery.EscrowContainerId, ct);
            var isResolved = summary == null ||
                             summary.TotalAssets == 0 ||
                             (summary.ClaimedAssets + summary.ReturnedAssets + summary.ExpiredAssets) >= summary.TotalAssets;

            if (!isResolved && !force)
            {
                throw new InvalidOperationException(
                    "Escrow-backed delivery is not fully resolved. Use force=true for test cleanup or resolve assets first.");
            }

            if (cleanupEscrow)
            {
                if (!isResolved)
                {
                    try
                    {
                        await _escrow.ReturnAssetsAsync(new EscrowActionRequest
                        {
                            RealmId = normalizedRealm,
                            ContainerId = delivery.EscrowContainerId,
                            IdempotencyKey = $"admin_delete:{deliveryId}:return",
                            ActorType = "admin",
                            ActorId = "admin-delivery-cleanup"
                        }, ct);
                    }
                    catch when (force)
                    {
                        // Best-effort during forced cleanup.
                    }
                }

                try
                {
                    await _escrow.CancelEscrowAsync(new EscrowActionRequest
                    {
                        RealmId = normalizedRealm,
                        ContainerId = delivery.EscrowContainerId,
                        IdempotencyKey = $"admin_delete:{deliveryId}:cancel",
                        ActorType = "admin",
                        ActorId = "admin-delivery-cleanup"
                    }, ct);
                }
                catch when (force)
                {
                    // Best-effort during forced cleanup.
                }

                _ = await _escrowRepo.DeleteContainerAsync(normalizedRealm, delivery.EscrowContainerId, ct);
            }
        }

        return await _repo.DeleteAsync(normalizedRealm, deliveryId, ct);
    }

    private async Task<DeliveryRecord> MutateDeliveryAsync(
        string realmId,
        string deliveryId,
        string idempotencyKey,
        Action<DeliveryRecord> mutate,
        CancellationToken ct)
    {
        using var _ = await AcquireLockAsync($"{realmId}:{deliveryId}", ct);

        const int attempts = 5;
        for (var i = 1; i <= attempts; i++)
        {
            var snap = await _repo.GetSnapshotAsync(realmId, deliveryId, ct)
                ?? throw new InvalidOperationException("Delivery not found.");

            var record = snap.Record;
            if (record.Metadata.TryGetValue($"op:{idempotencyKey}", out var status) &&
                string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                return record;

            mutate(record);
            record.Metadata[$"op:{idempotencyKey}"] = "completed";
            record.Version++;
            record.UpdatedUtc = DateTime.UtcNow;

            var ok = await _repo.TryReplaceAsync(realmId, record, snap.ConcurrencyToken, ct);
            if (ok) return record;
        }

        throw new InvalidOperationException("Delivery mutation conflicted repeatedly.");
    }

    private async Task TryMutateDeliveryAsync(
        string realmId,
        string deliveryId,
        string idempotencyKey,
        Action<DeliveryRecord> mutate,
        CancellationToken ct)
    {
        try
        {
            await MutateDeliveryAsync(realmId, deliveryId, idempotencyKey, mutate, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Best-effort delivery mutation failed. realm={RealmId} delivery={DeliveryId}", realmId, deliveryId);
        }
    }

    private static DeliveryStateTransition NewTransition(DeliveryState state, string actorType, string actorId, string? note = null)
        => new()
        {
            State = state,
            Utc = DateTime.UtcNow,
            ActorType = actorType,
            ActorId = actorId,
            Note = note
        };

    private static void ValidateIdempotency(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("IdempotencyKey is required.", nameof(idempotencyKey));
    }

    private static string NormalizeRealm(string? realmId)
        => string.IsNullOrWhiteSpace(realmId) ? "default" : realmId;

    private static string MakeStableId(string prefix, string idempotencyKey, string salt)
    {
        var raw = $"{prefix}:{idempotencyKey}:{salt}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return $"{prefix}_{hash[..24]}";
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

    public async Task<DeliveryAdminSearchResult> SearchAdminAsync(DeliveryAdminSearchQuery query, CancellationToken ct = default)
    {
        query ??= new DeliveryAdminSearchQuery();

        var realmId = NormalizeRealm(query.RealmId);
        var all = await _repo.ListAllAsync(realmId, ct);

        IEnumerable<DeliveryRecord> q = all;

        if (!string.IsNullOrWhiteSpace(query.CharacterId))
        {
            var cid = query.CharacterId.Trim();
            q = q.Where(x => string.Equals(x.Recipient.Id, cid, StringComparison.Ordinal) ||
                             string.Equals(x.Sender.Id, cid, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.SenderContains))
        {
            var sender = query.SenderContains.Trim();
            q = q.Where(x =>
                x.Sender.Id.Contains(sender, StringComparison.OrdinalIgnoreCase) ||
                x.Sender.DisplayName.Contains(sender, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Type.HasValue)
            q = q.Where(x => x.Type == query.Type.Value);

        if (query.CreatedFromUtc.HasValue)
            q = q.Where(x => x.CreatedUtc >= query.CreatedFromUtc.Value);

        if (query.CreatedToUtc.HasValue)
            q = q.Where(x => x.CreatedUtc <= query.CreatedToUtc.Value);

        var total = q.Count();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var items = q.OrderByDescending(x => x.CreatedUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new DeliveryAdminSearchResult
        {
            Total = total,
            Items = items
        };
    }

    public async Task<DeliveryAdminDetail?> GetAdminDetailAsync(string realmId, string deliveryId, CancellationToken ct = default)
    {
        var delivery = await _repo.GetAsync(realmId, deliveryId, ct);
        if (delivery == null) return null;

        EscrowContainer? escrowContainer = null;
        EscrowSummary? escrowSummary = null;

        if (!string.IsNullOrWhiteSpace(delivery.EscrowContainerId))
        {
            escrowContainer = await _escrow.GetEscrowContainerAsync(delivery.RealmId, delivery.EscrowContainerId, ct);
            escrowSummary = await _escrow.GetEscrowSummaryAsync(delivery.RealmId, delivery.EscrowContainerId, ct);
        }

        return new DeliveryAdminDetail
        {
            Delivery = delivery,
            EscrowContainer = escrowContainer,
            EscrowSummary = escrowSummary
        };
    }

    private static List<CreateSystemDeliveryRecipient> ResolveSystemRecipients(CreateSystemDeliveryRequest request)
    {
        if (request.Recipients != null && request.Recipients.Count > 0)
        {
            return request.Recipients
                .Where(x => !string.IsNullOrWhiteSpace(x.RecipientCharacterId))
                .GroupBy(x => x.RecipientCharacterId, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.RecipientCharacterId))
        {
            return
            [
                new CreateSystemDeliveryRecipient
                {
                    RecipientAccountId = request.RecipientAccountId,
                    RecipientCharacterId = request.RecipientCharacterId,
                    RecipientInventory = request.RecipientInventory
                }
            ];
        }

        throw new InvalidOperationException("At least one recipient is required.");
    }
}