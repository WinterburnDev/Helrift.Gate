using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Helrift.Gate.Api.Services.Escrow;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;
using Microsoft.Extensions.Options;

namespace Helrift.Gate.Api.Services.Bounties;

public sealed class BountyService : IBountyService
{
    private readonly IBountyRepository _repo;
    private readonly IGameDataProvider _data;
    private readonly IEscrowService _escrow;
    private readonly IEscrowRepository _escrowRepo;
    private readonly BountyOptions _options;
    private readonly ILogger<BountyService> _logger;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    public BountyService(
        IBountyRepository repo,
        IGameDataProvider data,
        IEscrowService escrow,
        IEscrowRepository escrowRepo,
        IOptions<BountyOptions> options,
        ILogger<BountyService> logger)
    {
        _repo = repo;
        _data = data;
        _escrow = escrow;
        _escrowRepo = escrowRepo;
        _options = options.Value ?? new BountyOptions();
        _logger = logger;
    }

    public async Task<BountyOrderSnapshot> CreateBountyAsync(CreateBountyContractRequest request, CancellationToken ct = default)
    {
        ValidateCreateRequest(request);

        var realmId = NormalizeRealm(request.RealmId);
        var bountyId = MakeStableId("bounty", request.IdempotencyKey, request.IssuerCharacterId, request.TargetCharacterId);
        var lockKey = $"{realmId}:{bountyId}";

        using var lockHandle = await AcquireLockAsync(lockKey, ct);

        var existing = await _repo.GetBountyAsync(realmId, bountyId, ct);
        if (existing != null)
            return ToSnapshot(existing);

        if (string.Equals(request.IssuerCharacterId, request.TargetCharacterId, StringComparison.Ordinal))
            throw new InvalidOperationException("Issuer cannot place a bounty on self.");

        if (request.RewardGold < Math.Max(1, _options.MinBountyGold))
            throw new InvalidOperationException($"RewardGold must be at least {_options.MinBountyGold}.");

        if (_options.MaxBountyGold > 0 && request.RewardGold > _options.MaxBountyGold)
            throw new InvalidOperationException($"RewardGold exceeds configured maximum {_options.MaxBountyGold}.");

        var issuer = await _data.GetCharacterAsync(request.IssuerAccountId, request.IssuerCharacterId, ct)
            ?? throw new InvalidOperationException("Issuer character was not found.");

        var target = await _data.GetCharacterAsync(request.TargetAccountId, request.TargetCharacterId, ct)
            ?? throw new InvalidOperationException("Target character was not found.");

        await EnsureIssuerLimitsAsync(realmId, request.IssuerCharacterId, request.TargetCharacterId, ct);

        var durationHours = ResolveDurationHours(request.DurationHours);
        var now = DateTime.UtcNow;
        var listingFee = ComputePercent(request.RewardGold, _options.ListingFeePercent);

        var bounty = new BountyContract
        {
            BountyId = bountyId,
            RealmId = realmId,
            IdempotencyKey = request.IdempotencyKey,
            IssuerAccountId = request.IssuerAccountId,
            IssuerCharacterId = request.IssuerCharacterId,
            TargetAccountId = request.TargetAccountId,
            TargetCharacterId = request.TargetCharacterId,
            Status = BountyStatus.Active,
            RewardGold = request.RewardGold,
            ListingFeePaid = 0,
            TaxAmount = 0,
            EscrowContainerId = $"escrow_bounty_{bountyId}",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(durationHours),
            MaxClaims = 1,
            Notes = request.Notes,
            Version = 1
        };

        if (!await _repo.TryCreateBountyAsync(bounty, ct))
        {
            var raced = await _repo.GetBountyAsync(realmId, bountyId, ct)
                ?? throw new InvalidOperationException("Failed to create bounty contract.");
            return ToSnapshot(raced);
        }

        try
        {
            await _escrow.CreateEscrowContainerAsync(new CreateEscrowContainerRequest
            {
                RealmId = realmId,
                ContainerId = bounty.EscrowContainerId,
                EscrowType = "bounty_reward",
                SourceFeature = "bounty",
                SourceEntityType = "bounty_contract",
                SourceEntityId = bounty.BountyId,
                ExpiresUtc = bounty.ExpiresAtUtc,
                ResolutionPolicy = EscrowResolutionPolicy.ReturnToSource,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["bountyId"] = bounty.BountyId,
                    ["targetCharacterId"] = bounty.TargetCharacterId,
                    ["issuerCharacterId"] = bounty.IssuerCharacterId
                }
            }, ct);

            var add = await _escrow.AddAssetsAsync(new AddEscrowAssetsRequest
            {
                RealmId = realmId,
                ContainerId = bounty.EscrowContainerId,
                IdempotencyKey = $"{request.IdempotencyKey}:add_reward",
                ActorType = "bounty",
                ActorId = bounty.BountyId,
                Assets =
                [
                    new EscrowAssetDraft
                    {
                        AssetType = EscrowAssetType.ItemInstance,
                        HoldingMode = EscrowHoldingMode.Escrowed,
                        SubtypeKey = _options.GoldItemDefinitionId,
                        QuantityValue = bounty.RewardGold,
                        ItemInstanceId = request.IssuerGoldItemInstanceId,
                        SourceAccountId = request.IssuerAccountId,
                        SourceCharacterId = request.IssuerCharacterId,
                        SourceInventory = request.SourceInventory,
                        RecipientAccountId = string.Empty,
                        RecipientCharacterId = string.Empty,
                        RecipientInventory = "inventory",
                        Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["bountyId"] = bounty.BountyId,
                            ["assetRole"] = "reward"
                        }
                    }
                ]
            }, ct);

            await _escrow.EscrowAssetsAsync(new EscrowActionRequest
            {
                RealmId = realmId,
                ContainerId = bounty.EscrowContainerId,
                IdempotencyKey = $"{request.IdempotencyKey}:escrow_reward",
                ActorType = "bounty",
                ActorId = bounty.BountyId
            }, ct);

            await _escrow.MakeAssetsClaimableAsync(new EscrowActionRequest
            {
                RealmId = realmId,
                ContainerId = bounty.EscrowContainerId,
                IdempotencyKey = $"{request.IdempotencyKey}:reward_claimable",
                ActorType = "bounty",
                ActorId = bounty.BountyId
            }, ct);

            var rewardAssetId = add.Assets.FirstOrDefault()?.Id;
            if (string.IsNullOrWhiteSpace(rewardAssetId))
                throw new InvalidOperationException("Bounty reward escrow asset was not created.");

            if (listingFee > 0)
            {
                await SinkListingFeeAsync(
                    realmId,
                    bounty.BountyId,
                    request.IssuerAccountId,
                    request.IssuerCharacterId,
                    request.IssuerGoldItemInstanceId,
                    request.SourceInventory,
                    request.IdempotencyKey,
                    listingFee,
                    ct);
            }

            var updated = await MutateBountyAsync(realmId, bounty.BountyId, $"{request.IdempotencyKey}:record_create", record =>
            {
                record.RewardEscrowAssetId = rewardAssetId;
                record.ListingFeePaid = listingFee;
                record.UpdatedAtForVersion();
            }, ct);

            _logger.LogInformation(
                "Bounty created. realm={RealmId} bounty={BountyId} issuer={IssuerCharacterId} target={TargetCharacterId} reward={RewardGold}",
                realmId,
                updated.BountyId,
                updated.IssuerCharacterId,
                updated.TargetCharacterId,
                updated.RewardGold);

            return ToSnapshot(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Bounty creation failed after record write. realm={RealmId} bounty={BountyId}",
                realmId,
                bounty.BountyId);

            await TryMarkFailedAsync(realmId, bounty.BountyId, $"{request.IdempotencyKey}:creation_failed", ex.Message, ct);
            throw;
        }
    }

    public async Task<BountyOrderSnapshot> CancelBountyAsync(CancelBountyContractRequest request, CancellationToken ct = default)
    {
        ValidateIdempotency(request.IdempotencyKey);
        if (string.IsNullOrWhiteSpace(request.BountyId)) throw new InvalidOperationException("BountyId is required.");

        var realmId = NormalizeRealm(request.RealmId);
        var lockKey = $"{realmId}:{request.BountyId}";

        using var _ = await AcquireLockAsync(lockKey, ct);

        var bounty = await _repo.GetBountyAsync(realmId, request.BountyId, ct)
            ?? throw new InvalidOperationException("Bounty was not found.");

        if (!request.IsAdminOverride && !string.Equals(bounty.IssuerCharacterId, request.ActorCharacterId, StringComparison.Ordinal))
            throw new InvalidOperationException("Only issuer can cancel this bounty.");

        if (bounty.Status != BountyStatus.Active)
            return ToSnapshot(bounty);

        await _escrow.ReturnAssetsAsync(new EscrowActionRequest
        {
            RealmId = realmId,
            ContainerId = bounty.EscrowContainerId,
            IdempotencyKey = $"{request.IdempotencyKey}:return_reward",
            ActorType = request.IsAdminOverride ? "admin" : "bounty",
            ActorId = request.IsAdminOverride ? "admin-bounty" : request.ActorCharacterId
        }, ct);

        var updated = await MutateBountyAsync(realmId, bounty.BountyId, request.IdempotencyKey, record =>
        {
            record.Status = BountyStatus.Cancelled;
            record.CancelledAtUtc = DateTime.UtcNow;
            record.UpdatedAtForVersion();
        }, ct);

        _logger.LogInformation(
            "Bounty cancelled. realm={RealmId} bounty={BountyId} actor={ActorCharacterId}",
            realmId,
            updated.BountyId,
            request.ActorCharacterId);

        return ToSnapshot(updated);
    }

    public async Task<ResolveBountyKillResult> ResolveKillAsync(ResolveBountyKillRequest request, CancellationToken ct = default)
    {
        ValidateIdempotency(request.IdempotencyKey);

        var realmId = NormalizeRealm(request.RealmId);
        var result = new ResolveBountyKillResult
        {
            RealmId = realmId,
            TargetCharacterId = request.TargetCharacterId,
            KillerCharacterId = request.KillerCharacterId
        };

        if (!request.IsValidPvpContext || !request.HasAuthoritativeKiller)
            return result;

        if (string.IsNullOrWhiteSpace(request.TargetCharacterId) || string.IsNullOrWhiteSpace(request.KillerCharacterId))
            return result;

        if (string.Equals(request.TargetCharacterId, request.KillerCharacterId, StringComparison.Ordinal))
            return result;

        if (_options.DisallowSameAccountKillPayout &&
            !string.IsNullOrWhiteSpace(request.KillerAccountId) &&
            string.Equals(request.KillerAccountId, request.TargetAccountId, StringComparison.Ordinal))
        {
            return result;
        }

        await ExpireDueBountiesAsync(realmId, ct);

        var candidates = await _repo.ListBountiesByTargetAsync(realmId, request.TargetCharacterId, ct);
        if (candidates.Count == 0)
            return result;

        var now = DateTime.UtcNow;
        foreach (var candidate in candidates.Where(x => x.Status == BountyStatus.Active))
        {
            if (candidate.ExpiresAtUtc <= now)
            {
                result.SkippedBountyIds.Add(candidate.BountyId);
                continue;
            }

            if (!string.Equals(candidate.TargetCharacterId, request.TargetCharacterId, StringComparison.Ordinal))
            {
                result.SkippedBountyIds.Add(candidate.BountyId);
                continue;
            }

            var lockKey = $"{realmId}:{candidate.BountyId}";
            using var _ = await AcquireLockAsync(lockKey, ct);

            var current = await _repo.GetBountyAsync(realmId, candidate.BountyId, ct);
            if (current == null || current.Status != BountyStatus.Active)
            {
                result.SkippedBountyIds.Add(candidate.BountyId);
                continue;
            }

            if (_options.ResolveKillPairCooldownSeconds > 0)
            {
                var lockoutUtc = now.AddSeconds(-_options.ResolveKillPairCooldownSeconds);
                if (current.FulfilledAtUtc.HasValue && current.FulfilledAtUtc.Value >= lockoutUtc)
                {
                    result.SkippedBountyIds.Add(candidate.BountyId);
                    continue;
                }
            }

            try
            {
                await _escrow.ReleaseAssetsAsync(new EscrowActionRequest
                {
                    RealmId = realmId,
                    ContainerId = current.EscrowContainerId,
                    IdempotencyKey = $"{request.IdempotencyKey}:release:{current.BountyId}",
                    ActorType = "bounty",
                    ActorId = current.BountyId,
                    AssetIds = string.IsNullOrWhiteSpace(current.RewardEscrowAssetId) ? null : [current.RewardEscrowAssetId],
                    TargetAccountId = request.KillerAccountId,
                    TargetCharacterId = request.KillerCharacterId,
                    TargetInventory = "inventory"
                }, ct);

                var updated = await MutateBountyAsync(realmId, current.BountyId, $"{request.IdempotencyKey}:fulfill:{current.BountyId}", record =>
                {
                    record.Status = BountyStatus.Fulfilled;
                    record.FulfilledAtUtc = DateTime.UtcNow;
                    record.FulfilledByAccountId = request.KillerAccountId;
                    record.FulfilledByCharacterId = request.KillerCharacterId;
                    record.FulfillmentContext = new BountyFulfillmentContext
                    {
                        RealmId = realmId,
                        EventId = request.EventId,
                        TargetAccountId = request.TargetAccountId,
                        TargetCharacterId = request.TargetCharacterId,
                        KillerAccountId = request.KillerAccountId,
                        KillerCharacterId = request.KillerCharacterId,
                        OccurredAtUtc = request.OccurredAtUtc == default ? DateTime.UtcNow : request.OccurredAtUtc,
                        MapId = request.MapId
                    };
                    record.UpdatedAtForVersion();
                }, ct);

                result.BountiesResolved++;
                result.ResolvedBountyIds.Add(updated.BountyId);

                _logger.LogInformation(
                    "Bounty fulfilled. realm={RealmId} bounty={BountyId} target={TargetCharacterId} killer={KillerCharacterId}",
                    realmId,
                    updated.BountyId,
                    request.TargetCharacterId,
                    request.KillerCharacterId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Bounty fulfillment failed. realm={RealmId} bounty={BountyId} target={TargetCharacterId} killer={KillerCharacterId}",
                    realmId,
                    current.BountyId,
                    request.TargetCharacterId,
                    request.KillerCharacterId);

                result.SkippedBountyIds.Add(current.BountyId);
            }
        }

        return result;
    }

    public async Task<BountyBrowseResult> BrowseActiveAsync(BountyBrowseQuery query, CancellationToken ct = default)
    {
        query ??= new BountyBrowseQuery();

        var realmId = NormalizeRealm(query.RealmId);
        await ExpireDueBountiesAsync(realmId, ct);

        var all = await _repo.ListAllBountiesAsync(realmId, ct);
        IEnumerable<BountyContract> q = all;

        var targetStatus = query.Status ?? BountyStatus.Active;
        q = q.Where(x => x.Status == targetStatus);

        if (!string.IsNullOrWhiteSpace(query.TargetCharacterId))
            q = q.Where(x => string.Equals(x.TargetCharacterId, query.TargetCharacterId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(query.IssuerCharacterId))
            q = q.Where(x => string.Equals(x.IssuerCharacterId, query.IssuerCharacterId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(query.FulfilledByCharacterId))
            q = q.Where(x => string.Equals(x.FulfilledByCharacterId, query.FulfilledByCharacterId, StringComparison.Ordinal));

        var total = q.Count();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var items = q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToSnapshot)
            .ToList();

        return new BountyBrowseResult
        {
            Total = total,
            Items = items
        };
    }

    public async Task<BountyBrowseResult> GetMyIssuedAsync(string realmId, string issuerCharacterId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(issuerCharacterId))
            throw new InvalidOperationException("issuerCharacterId is required.");

        var records = await _repo.ListBountiesByIssuerAsync(NormalizeRealm(realmId), issuerCharacterId, ct);
        var total = records.Count;

        var items = records
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 200))
            .Take(Math.Clamp(pageSize, 1, 200))
            .Select(ToSnapshot)
            .ToList();

        return new BountyBrowseResult
        {
            Total = total,
            Items = items
        };
    }

    public async Task<BountyAdminSearchResult> SearchAdminAsync(BountyAdminSearchQuery query, CancellationToken ct = default)
    {
        query ??= new BountyAdminSearchQuery();

        var realmId = NormalizeRealm(query.RealmId);
        await ExpireDueBountiesAsync(realmId, ct);

        var all = await _repo.ListAllBountiesAsync(realmId, ct);
        IEnumerable<BountyContract> q = all;

        if (query.Status.HasValue)
            q = q.Where(x => x.Status == query.Status.Value);

        if (!string.IsNullOrWhiteSpace(query.TargetCharacterId))
            q = q.Where(x => string.Equals(x.TargetCharacterId, query.TargetCharacterId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(query.IssuerCharacterId))
            q = q.Where(x => string.Equals(x.IssuerCharacterId, query.IssuerCharacterId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(query.FulfilledByCharacterId))
            q = q.Where(x => string.Equals(x.FulfilledByCharacterId, query.FulfilledByCharacterId, StringComparison.Ordinal));

        if (query.CreatedFromUtc.HasValue)
            q = q.Where(x => x.CreatedAtUtc >= query.CreatedFromUtc.Value);

        if (query.CreatedToUtc.HasValue)
            q = q.Where(x => x.CreatedAtUtc <= query.CreatedToUtc.Value);

        var total = q.Count();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 250);

        var items = q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToSnapshot)
            .ToList();

        return new BountyAdminSearchResult
        {
            Total = total,
            Items = items
        };
    }

    public async Task<BountyAdminDetail?> GetAdminDetailAsync(string realmId, string bountyId, CancellationToken ct = default)
    {
        var bounty = await _repo.GetBountyAsync(NormalizeRealm(realmId), bountyId, ct);
        if (bounty == null) return null;

        EscrowContainer? escrowContainer = null;
        EscrowSummary? escrowSummary = null;

        if (!string.IsNullOrWhiteSpace(bounty.EscrowContainerId))
        {
            escrowContainer = await _escrow.GetEscrowContainerAsync(bounty.RealmId, bounty.EscrowContainerId, ct);
            escrowSummary = await _escrow.GetEscrowSummaryAsync(bounty.RealmId, bounty.EscrowContainerId, ct);
        }

        return new BountyAdminDetail
        {
            Bounty = ToSnapshot(bounty),
            EscrowContainer = escrowContainer,
            EscrowSummary = escrowSummary
        };
    }

    public async Task<int> ExpireDueBountiesAsync(string realmId, CancellationToken ct = default)
    {
        var normalizedRealm = NormalizeRealm(realmId);
        var all = await _repo.ListAllBountiesAsync(normalizedRealm, ct);
        var due = all.Where(x => x.Status == BountyStatus.Active && x.ExpiresAtUtc <= DateTime.UtcNow).ToList();
        if (due.Count == 0) return 0;

        var expired = 0;
        foreach (var bounty in due)
        {
            var lockKey = $"{normalizedRealm}:{bounty.BountyId}";
            using var _ = await AcquireLockAsync(lockKey, ct);

            var current = await _repo.GetBountyAsync(normalizedRealm, bounty.BountyId, ct);
            if (current == null || current.Status != BountyStatus.Active || current.ExpiresAtUtc > DateTime.UtcNow)
                continue;

            try
            {
                await _escrow.ReturnAssetsAsync(new EscrowActionRequest
                {
                    RealmId = normalizedRealm,
                    ContainerId = current.EscrowContainerId,
                    IdempotencyKey = $"expire:{current.BountyId}:{DateTime.UtcNow:yyyyMMddHHmmss}",
                    ActorType = "bounty",
                    ActorId = current.BountyId
                }, ct);

                await MutateBountyAsync(normalizedRealm, current.BountyId, $"expire_state:{current.BountyId}", record =>
                {
                    record.Status = BountyStatus.Expired;
                    record.Metadata["expiredAtUtc"] = DateTime.UtcNow.ToString("o");
                    record.UpdatedAtForVersion();
                }, ct);

                expired++;

                _logger.LogInformation(
                    "Bounty expired. realm={RealmId} bounty={BountyId}",
                    normalizedRealm,
                    current.BountyId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Bounty expiry failed. realm={RealmId} bounty={BountyId}",
                    normalizedRealm,
                    current.BountyId);
            }
        }

        return expired;
    }

    private async Task EnsureIssuerLimitsAsync(string realmId, string issuerCharacterId, string targetCharacterId, CancellationToken ct)
    {
        var issuerBounties = await _repo.ListBountiesByIssuerAsync(realmId, issuerCharacterId, ct);
        var activeCount = issuerBounties.Count(x => x.Status == BountyStatus.Active);

        if (activeCount >= Math.Max(1, _options.MaxActiveBountiesPerIssuer))
            throw new InvalidOperationException("Max active bounties reached for issuer.");

        if (!_options.AllowMultipleActivePerTarget && issuerBounties.Any(x =>
            x.Status == BountyStatus.Active && string.Equals(x.TargetCharacterId, targetCharacterId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("An active bounty already exists for that target.");
        }
    }

    private async Task SinkListingFeeAsync(
        string realmId,
        string bountyId,
        string sourceAccountId,
        string sourceCharacterId,
        string sourceGoldItemInstanceId,
        string sourceInventory,
        string idempotencyKey,
        long feeAmount,
        CancellationToken ct)
    {
        if (feeAmount <= 0)
            return;

        var feeContainerId = $"escrow_bounty_fee_{bountyId}_{ShortHash(idempotencyKey)}";

        await _escrow.CreateEscrowContainerAsync(new CreateEscrowContainerRequest
        {
            RealmId = realmId,
            ContainerId = feeContainerId,
            EscrowType = "bounty_fee_sink",
            SourceFeature = "bounty",
            SourceEntityType = "bounty_fee",
            SourceEntityId = bountyId,
            ResolutionPolicy = EscrowResolutionPolicy.DestroyOnExpiry,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bountyId"] = bountyId,
                ["purpose"] = "listing_fee"
            }
        }, ct);

        await _escrow.AddAssetsAsync(new AddEscrowAssetsRequest
        {
            RealmId = realmId,
            ContainerId = feeContainerId,
            IdempotencyKey = $"{idempotencyKey}:fee_add",
            ActorType = "bounty",
            ActorId = bountyId,
            Assets =
            [
                new EscrowAssetDraft
                {
                    AssetType = EscrowAssetType.ItemInstance,
                    HoldingMode = EscrowHoldingMode.Escrowed,
                    SubtypeKey = _options.GoldItemDefinitionId,
                    QuantityValue = feeAmount,
                    ItemInstanceId = sourceGoldItemInstanceId,
                    SourceAccountId = sourceAccountId,
                    SourceCharacterId = sourceCharacterId,
                    SourceInventory = sourceInventory,
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["bountyId"] = bountyId,
                        ["assetRole"] = "listing_fee"
                    }
                }
            ]
        }, ct);

        await _escrow.EscrowAssetsAsync(new EscrowActionRequest
        {
            RealmId = realmId,
            ContainerId = feeContainerId,
            IdempotencyKey = $"{idempotencyKey}:fee_escrow",
            ActorType = "bounty",
            ActorId = bountyId
        }, ct);

        await _escrow.ExpireEscrowAsync(new EscrowActionRequest
        {
            RealmId = realmId,
            ContainerId = feeContainerId,
            IdempotencyKey = $"{idempotencyKey}:fee_expire",
            ActorType = "bounty",
            ActorId = bountyId
        }, ct);

        _ = await _escrowRepo.DeleteContainerAsync(realmId, feeContainerId, ct);
    }

    private async Task<BountyContract> MutateBountyAsync(
        string realmId,
        string bountyId,
        string idempotencyKey,
        Action<BountyContract> mutate,
        CancellationToken ct)
    {
        const int attempts = 5;

        for (var i = 1; i <= attempts; i++)
        {
            var snap = await _repo.GetBountySnapshotAsync(realmId, bountyId, ct)
                ?? throw new InvalidOperationException("Bounty contract was not found.");

            var record = snap.Record;
            if (record.Metadata.TryGetValue($"op:{idempotencyKey}", out var status) &&
                string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return record;
            }

            mutate(record);
            record.Metadata[$"op:{idempotencyKey}"] = "completed";
            record.Version++;

            var ok = await _repo.TryReplaceBountyAsync(realmId, record, snap.ConcurrencyToken, ct);
            if (ok) return record;
        }

        throw new InvalidOperationException("Bounty mutation conflicted repeatedly.");
    }

    private async Task TryMarkFailedAsync(string realmId, string bountyId, string opKey, string reason, CancellationToken ct)
    {
        try
        {
            await MutateBountyAsync(realmId, bountyId, opKey, record =>
            {
                record.Metadata["lastFailure"] = reason;
                record.UpdatedAtForVersion();
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to mark bounty failure metadata. realm={RealmId} bounty={BountyId}",
                realmId,
                bountyId);
        }
    }

    private static BountyOrderSnapshot ToSnapshot(BountyContract bounty)
        => new() { Bounty = bounty };

    private int ResolveDurationHours(int? requested)
    {
        var value = requested ?? _options.DefaultBountyDurationHours;
        if (value <= 0) value = _options.DefaultBountyDurationHours;
        return Math.Clamp(value, 1, Math.Max(1, _options.MaxBountyDurationHours));
    }

    private static long ComputePercent(long amount, decimal percent)
    {
        if (amount <= 0 || percent <= 0m)
            return 0;

        var value = (decimal)amount * (percent / 100m);
        return (long)Math.Ceiling(value);
    }

    private static void ValidateCreateRequest(CreateBountyContractRequest request)
    {
        if (request == null) throw new InvalidOperationException("Request is required.");
        ValidateIdempotency(request.IdempotencyKey);

        if (string.IsNullOrWhiteSpace(request.IssuerAccountId)) throw new InvalidOperationException("IssuerAccountId is required.");
        if (string.IsNullOrWhiteSpace(request.IssuerCharacterId)) throw new InvalidOperationException("IssuerCharacterId is required.");
        if (string.IsNullOrWhiteSpace(request.TargetAccountId)) throw new InvalidOperationException("TargetAccountId is required.");
        if (string.IsNullOrWhiteSpace(request.TargetCharacterId)) throw new InvalidOperationException("TargetCharacterId is required.");
        if (request.RewardGold <= 0) throw new InvalidOperationException("RewardGold must be greater than zero.");
        if (string.IsNullOrWhiteSpace(request.IssuerGoldItemInstanceId)) throw new InvalidOperationException("IssuerGoldItemInstanceId is required.");
    }

    private static void ValidateIdempotency(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new InvalidOperationException("IdempotencyKey is required.");
    }

    private static string NormalizeRealm(string? realmId)
        => string.IsNullOrWhiteSpace(realmId) ? "default" : realmId.Trim();

    private static string MakeStableId(string prefix, string idempotencyKey, string left, string right)
    {
        var raw = $"{prefix}:{idempotencyKey}:{left}:{right}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return $"{prefix}_{hash[..24]}";
    }

    private static string ShortHash(string raw)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw ?? string.Empty))).ToLowerInvariant();
        return hash[..12];
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

internal static class BountyContractMutations
{
    public static void UpdatedAtForVersion(this BountyContract contract)
    {
        contract.Metadata["updatedAtUtc"] = DateTime.UtcNow.ToString("o");
    }
}
