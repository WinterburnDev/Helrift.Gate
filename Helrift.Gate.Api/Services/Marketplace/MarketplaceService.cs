using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Helrift.Gate.Api.Services.Escrow;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;
using Microsoft.Extensions.Options;

namespace Helrift.Gate.Api.Services.Marketplace;

public sealed class MarketplaceService : IMarketplaceService
{
    private readonly IMarketplaceRepository _repo;
    private readonly IEscrowService _escrow;
    private readonly IEscrowRepository _escrowRepo;
    private readonly MarketplaceOptions _options;
    private readonly ILogger<MarketplaceService> _logger;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    public MarketplaceService(
        IMarketplaceRepository repo,
        IEscrowService escrow,
        IEscrowRepository escrowRepo,
        IOptions<MarketplaceOptions> options,
        ILogger<MarketplaceService> logger)
    {
        _repo = repo;
        _escrow = escrow;
        _escrowRepo = escrowRepo;
        _options = options.Value ?? new MarketplaceOptions();
        _logger = logger;
    }

    public async Task<MarketplaceOrderSnapshot> CreateSellOrderAsync(CreateMarketplaceSellOrderRequest request, CancellationToken ct = default)
    {
        ValidateBaseCreate(request.IdempotencyKey, request.RealmId, request.OwnerCharacterId, request.OwnerAccountId, request.ItemDefinitionId, request.Quantity, request.UnitPriceGold);

        if (string.IsNullOrWhiteSpace(request.ItemInstanceId))
            throw new InvalidOperationException("ItemInstanceId is required for sell orders.");

        var realmId = NormalizeRealm(request.RealmId);
        await EnsureActiveOrderLimitAsync(realmId, request.OwnerCharacterId, ct);

        var orderId = MakeStableId("mkt_sell", request.IdempotencyKey, request.OwnerCharacterId, request.ItemDefinitionId);
        var lockKey = $"{realmId}:{orderId}";

        using var _ = await AcquireLockAsync(lockKey, ct);

        var existing = await _repo.GetOrderAsync(realmId, orderId, ct);
        if (existing != null)
            return ToSnapshot(existing, quantityOverride: existing.QuantityRemaining);

        if (request.Quantity != 1)
            throw new InvalidOperationException("V1 marketplace requires quantity=1 for escrow-backed item safety.");

        var durationHours = ResolveDurationHours(request.DurationHours);
        var now = DateTime.UtcNow;

        var listingTax = ComputeTax(grossGold: checked(request.UnitPriceGold * request.Quantity));

        var order = new MarketplaceOrder
        {
            OrderId = orderId,
            RealmId = realmId,
            IdempotencyKey = request.IdempotencyKey,
            OrderType = MarketplaceOrderType.Sell,
            Status = MarketplaceOrderStatus.Active,
            OwnerAccountId = request.OwnerAccountId,
            OwnerCharacterId = request.OwnerCharacterId,
            ItemDefinitionId = request.ItemDefinitionId,
            ItemInstanceId = request.ItemInstanceId,
            QuantityTotal = request.Quantity,
            QuantityRemaining = request.Quantity,
            UnitPriceGold = request.UnitPriceGold,
            ListingFeePaid = 0,
            CompletionTaxReserved = 0,
            EscrowContainerId = $"escrow_marketplace_order_{orderId}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(durationHours),
            Version = 1
        };

        if (!await _repo.TryCreateOrderAsync(order, ct))
        {
            var raced = await _repo.GetOrderAsync(realmId, orderId, ct)
                ?? throw new InvalidOperationException("Failed to create sell order.");
            return ToSnapshot(raced, quantityOverride: raced.QuantityRemaining);
        }

        await _escrow.CreateEscrowContainerAsync(new CreateEscrowContainerRequest
        {
            RealmId = realmId,
            ContainerId = order.EscrowContainerId,
            EscrowType = "marketplace_order",
            SourceFeature = "marketplace",
            SourceEntityType = "order",
            SourceEntityId = orderId,
            ExpiresUtc = order.ExpiresAtUtc,
            ResolutionPolicy = EscrowResolutionPolicy.ReturnToSource,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["orderId"] = orderId,
                ["orderType"] = MarketplaceOrderType.Sell.ToString()
            }
        }, ct);

        var addResult = await _escrow.AddAssetsAsync(new AddEscrowAssetsRequest
        {
            RealmId = realmId,
            ContainerId = order.EscrowContainerId,
            IdempotencyKey = $"{request.IdempotencyKey}:add_asset",
            ActorType = "marketplace",
            ActorId = orderId,
            Assets =
            [
                new EscrowAssetDraft
                {
                    AssetType = EscrowAssetType.ItemInstance,
                    HoldingMode = EscrowHoldingMode.Escrowed,
                    SubtypeKey = request.ItemDefinitionId,
                    QuantityValue = request.Quantity,
                    ItemInstanceId = request.ItemInstanceId,
                    SourceAccountId = request.OwnerAccountId,
                    SourceCharacterId = request.OwnerCharacterId,
                    SourceInventory = request.SourceInventory,
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["orderId"] = orderId,
                        ["orderType"] = "sell"
                    }
                }
            ]
        }, ct);

        await _escrow.EscrowAssetsAsync(new EscrowActionRequest
        {
            RealmId = realmId,
            ContainerId = order.EscrowContainerId,
            IdempotencyKey = $"{request.IdempotencyKey}:escrow",
            ActorType = "marketplace",
            ActorId = orderId
        }, ct);

        await _escrow.MakeAssetsClaimableAsync(new EscrowActionRequest
        {
            RealmId = realmId,
            ContainerId = order.EscrowContainerId,
            IdempotencyKey = $"{request.IdempotencyKey}:claimable",
            ActorType = "marketplace",
            ActorId = orderId
        }, ct);

        var sellAssetId = addResult.Assets.FirstOrDefault()?.Id;
        if (string.IsNullOrWhiteSpace(sellAssetId))
            throw new InvalidOperationException("Escrow asset id was not created for sell order.");

        var listingFee = listingTax.ListingFeeGold;
        if (listingFee > 0)
        {
            if (string.IsNullOrWhiteSpace(request.OwnerGoldItemInstanceId))
                throw new InvalidOperationException("OwnerGoldItemInstanceId is required when listing fee is enabled.");

            await SinkGoldAsync(
                realmId,
                request.OwnerAccountId,
                request.OwnerCharacterId,
                request.OwnerGoldItemInstanceId,
                listingFee,
                request.SourceInventory,
                request.IdempotencyKey,
                orderId,
                ct);
        }

        var updated = await MutateOrderAsync(realmId, orderId, $"{request.IdempotencyKey}:record_create", orderRecord =>
        {
            orderRecord.EscrowPrimaryAssetId = sellAssetId;
            orderRecord.ListingFeePaid = listingFee;
            orderRecord.UpdatedAtUtc = DateTime.UtcNow;
        }, ct);

        return ToSnapshot(updated, quantityOverride: updated.QuantityRemaining);
    }

    public async Task<MarketplaceOrderSnapshot> CreateBuyOrderAsync(CreateMarketplaceBuyOrderRequest request, CancellationToken ct = default)
    {
        ValidateBaseCreate(request.IdempotencyKey, request.RealmId, request.OwnerCharacterId, request.OwnerAccountId, request.ItemDefinitionId, request.Quantity, request.UnitPriceGold);

        if (string.IsNullOrWhiteSpace(request.OwnerGoldItemInstanceId))
            throw new InvalidOperationException("OwnerGoldItemInstanceId is required for buy orders.");

        var realmId = NormalizeRealm(request.RealmId);
        await EnsureActiveOrderLimitAsync(realmId, request.OwnerCharacterId, ct);

        var orderId = MakeStableId("mkt_buy", request.IdempotencyKey, request.OwnerCharacterId, request.ItemDefinitionId);
        var lockKey = $"{realmId}:{orderId}";

        using var _ = await AcquireLockAsync(lockKey, ct);

        var existing = await _repo.GetOrderAsync(realmId, orderId, ct);
        if (existing != null)
            return ToSnapshot(existing, quantityOverride: existing.QuantityRemaining);

        if (request.Quantity != 1)
            throw new InvalidOperationException("V1 marketplace requires quantity=1 for escrow-backed fulfillment safety.");

        var durationHours = ResolveDurationHours(request.DurationHours);
        var now = DateTime.UtcNow;

        var gross = checked(request.UnitPriceGold * request.Quantity);
        var tax = ComputeTax(gross);

        var order = new MarketplaceOrder
        {
            OrderId = orderId,
            RealmId = realmId,
            IdempotencyKey = request.IdempotencyKey,
            OrderType = MarketplaceOrderType.Buy,
            Status = MarketplaceOrderStatus.Active,
            OwnerAccountId = request.OwnerAccountId,
            OwnerCharacterId = request.OwnerCharacterId,
            ItemDefinitionId = request.ItemDefinitionId,
            QuantityTotal = request.Quantity,
            QuantityRemaining = request.Quantity,
            UnitPriceGold = request.UnitPriceGold,
            ListingFeePaid = 0,
            CompletionTaxReserved = tax.CompletionTaxGold,
            EscrowContainerId = $"escrow_marketplace_order_{orderId}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(durationHours),
            Version = 1
        };

        if (!await _repo.TryCreateOrderAsync(order, ct))
        {
            var raced = await _repo.GetOrderAsync(realmId, orderId, ct)
                ?? throw new InvalidOperationException("Failed to create buy order.");
            return ToSnapshot(raced, quantityOverride: raced.QuantityRemaining);
        }

        await _escrow.CreateEscrowContainerAsync(new CreateEscrowContainerRequest
        {
            RealmId = realmId,
            ContainerId = order.EscrowContainerId,
            EscrowType = "marketplace_order",
            SourceFeature = "marketplace",
            SourceEntityType = "order",
            SourceEntityId = orderId,
            ExpiresUtc = order.ExpiresAtUtc,
            ResolutionPolicy = EscrowResolutionPolicy.ReturnToSource,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["orderId"] = orderId,
                ["orderType"] = MarketplaceOrderType.Buy.ToString()
            }
        }, ct);

        var drafts = new List<EscrowAssetDraft>
        {
            new()
            {
                AssetType = EscrowAssetType.ItemInstance,
                HoldingMode = EscrowHoldingMode.Escrowed,
                SubtypeKey = _options.GoldItemDefinitionId,
                QuantityValue = gross,
                ItemInstanceId = request.OwnerGoldItemInstanceId,
                SourceAccountId = request.OwnerAccountId,
                SourceCharacterId = request.OwnerCharacterId,
                SourceInventory = request.SourceInventory,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["orderId"] = orderId,
                    ["orderType"] = "buy",
                    ["assetRole"] = "payout"
                }
            }
        };

        if (tax.CompletionTaxGold > 0)
        {
            drafts.Add(new EscrowAssetDraft
            {
                AssetType = EscrowAssetType.ItemInstance,
                HoldingMode = EscrowHoldingMode.Escrowed,
                SubtypeKey = _options.GoldItemDefinitionId,
                QuantityValue = tax.CompletionTaxGold,
                ItemInstanceId = request.OwnerGoldItemInstanceId,
                SourceAccountId = request.OwnerAccountId,
                SourceCharacterId = request.OwnerCharacterId,
                SourceInventory = request.SourceInventory,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["orderId"] = orderId,
                    ["orderType"] = "buy",
                    ["assetRole"] = "tax"
                }
            });
        }

        var addResult = await _escrow.AddAssetsAsync(new AddEscrowAssetsRequest
        {
            RealmId = realmId,
            ContainerId = order.EscrowContainerId,
            IdempotencyKey = $"{request.IdempotencyKey}:add_asset",
            ActorType = "marketplace",
            ActorId = orderId,
            Assets = drafts
        }, ct);

        await _escrow.EscrowAssetsAsync(new EscrowActionRequest
        {
            RealmId = realmId,
            ContainerId = order.EscrowContainerId,
            IdempotencyKey = $"{request.IdempotencyKey}:escrow",
            ActorType = "marketplace",
            ActorId = orderId
        }, ct);

        await _escrow.MakeAssetsClaimableAsync(new EscrowActionRequest
        {
            RealmId = realmId,
            ContainerId = order.EscrowContainerId,
            IdempotencyKey = $"{request.IdempotencyKey}:claimable",
            ActorType = "marketplace",
            ActorId = orderId
        }, ct);

        var payoutAsset = addResult.Assets.FirstOrDefault(a =>
            a.Metadata.TryGetValue("assetRole", out var role) && string.Equals(role, "payout", StringComparison.OrdinalIgnoreCase));

        var taxAsset = addResult.Assets.FirstOrDefault(a =>
            a.Metadata.TryGetValue("assetRole", out var role) && string.Equals(role, "tax", StringComparison.OrdinalIgnoreCase));

        if (payoutAsset == null)
            throw new InvalidOperationException("Escrow payout asset was not created for buy order.");

        var listingFee = tax.ListingFeeGold;
        if (listingFee > 0)
        {
            await SinkGoldAsync(
                realmId,
                request.OwnerAccountId,
                request.OwnerCharacterId,
                request.OwnerGoldItemInstanceId,
                listingFee,
                request.SourceInventory,
                request.IdempotencyKey,
                orderId,
                ct);
        }

        var updated = await MutateOrderAsync(realmId, orderId, $"{request.IdempotencyKey}:record_create", orderRecord =>
        {
            orderRecord.EscrowPrimaryAssetId = payoutAsset.Id;
            orderRecord.EscrowTaxAssetId = taxAsset?.Id;
            orderRecord.ListingFeePaid = listingFee;
            orderRecord.UpdatedAtUtc = DateTime.UtcNow;
        }, ct);

        return ToSnapshot(updated, quantityOverride: updated.QuantityRemaining);
    }

    public async Task<MarketplaceOrderSnapshot> FulfillOrderAsync(FulfillMarketplaceOrderRequest request, CancellationToken ct = default)
    {
        ValidateIdempotency(request.IdempotencyKey);
        if (string.IsNullOrWhiteSpace(request.OrderId)) throw new InvalidOperationException("OrderId is required.");
        if (string.IsNullOrWhiteSpace(request.FulfillerAccountId)) throw new InvalidOperationException("FulfillerAccountId is required.");
        if (string.IsNullOrWhiteSpace(request.FulfillerCharacterId)) throw new InvalidOperationException("FulfillerCharacterId is required.");

        var realmId = NormalizeRealm(request.RealmId);
        var lockKey = $"{realmId}:{request.OrderId}";

        using var _ = await AcquireLockAsync(lockKey, ct);

        var order = await _repo.GetOrderAsync(realmId, request.OrderId, ct)
            ?? throw new InvalidOperationException("Order was not found.");

        if (IsTerminal(order.Status))
            throw new InvalidOperationException($"Order is already {order.Status}.");

        if (DateTime.UtcNow >= order.ExpiresAtUtc)
        {
            await ExpireOrderInternalAsync(order, request.IdempotencyKey, ct);
            order = await _repo.GetOrderAsync(realmId, request.OrderId, ct)
                ?? throw new InvalidOperationException("Order expired and could not be reloaded.");
            throw new InvalidOperationException("Order has expired.");
        }

        if (!_options.AllowSelfFulfillment && string.Equals(order.OwnerCharacterId, request.FulfillerCharacterId, StringComparison.Ordinal))
            throw new InvalidOperationException("Self-fulfillment is not allowed.");

        if (request.Quantity <= 0)
            throw new InvalidOperationException("Quantity must be greater than zero.");

        if (request.Quantity != order.QuantityRemaining)
        {
            if (!_options.AllowPartialFills)
                throw new InvalidOperationException("Partial fills are disabled by Marketplace configuration.");

            throw new InvalidOperationException("Partial fills are not available in V1 escrow flow.");
        }

        var gross = checked((long)request.Quantity * order.UnitPriceGold);
        var tax = ComputeTax(gross);

        MarketplaceTransaction tx;

        if (order.OrderType == MarketplaceOrderType.Sell)
        {
            if (string.IsNullOrWhiteSpace(request.BuyerGoldItemInstanceId))
                throw new InvalidOperationException("BuyerGoldItemInstanceId is required to fulfill sell orders.");

            if (string.IsNullOrWhiteSpace(order.EscrowPrimaryAssetId))
                throw new InvalidOperationException("Sell order escrow asset is missing.");

            var settlementId = $"escrow_marketplace_settle_{request.OrderId}_{ShortHash(request.IdempotencyKey)}";

            await _escrow.CreateEscrowContainerAsync(new CreateEscrowContainerRequest
            {
                RealmId = realmId,
                ContainerId = settlementId,
                EscrowType = "marketplace_settlement",
                SourceFeature = "marketplace",
                SourceEntityType = "order_settlement",
                SourceEntityId = request.OrderId,
                ResolutionPolicy = EscrowResolutionPolicy.Manual,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["orderId"] = request.OrderId,
                    ["flow"] = "sell"
                }
            }, ct);

            var settlementDrafts = new List<EscrowAssetDraft>
            {
                new()
                {
                    AssetType = EscrowAssetType.ItemInstance,
                    HoldingMode = EscrowHoldingMode.Escrowed,
                    SubtypeKey = _options.GoldItemDefinitionId,
                    QuantityValue = tax.NetSettlementGold,
                    ItemInstanceId = request.BuyerGoldItemInstanceId,
                    SourceAccountId = request.FulfillerAccountId,
                    SourceCharacterId = request.FulfillerCharacterId,
                    SourceInventory = request.SourceInventory,
                    RecipientAccountId = order.OwnerAccountId,
                    RecipientCharacterId = order.OwnerCharacterId,
                    RecipientInventory = request.TargetInventory,
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["orderId"] = request.OrderId,
                        ["assetRole"] = "seller_payout"
                    }
                }
            };

            if (tax.CompletionTaxGold > 0)
            {
                settlementDrafts.Add(new EscrowAssetDraft
                {
                    AssetType = EscrowAssetType.ItemInstance,
                    HoldingMode = EscrowHoldingMode.Escrowed,
                    SubtypeKey = _options.GoldItemDefinitionId,
                    QuantityValue = tax.CompletionTaxGold,
                    ItemInstanceId = request.BuyerGoldItemInstanceId,
                    SourceAccountId = request.FulfillerAccountId,
                    SourceCharacterId = request.FulfillerCharacterId,
                    SourceInventory = request.SourceInventory,
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["orderId"] = request.OrderId,
                        ["assetRole"] = "tax"
                    }
                });
            }

            var settlementAdd = await _escrow.AddAssetsAsync(new AddEscrowAssetsRequest
            {
                RealmId = realmId,
                ContainerId = settlementId,
                IdempotencyKey = $"{request.IdempotencyKey}:settlement_add",
                ActorType = "marketplace",
                ActorId = request.OrderId,
                Assets = settlementDrafts
            }, ct);

            await _escrow.EscrowAssetsAsync(new EscrowActionRequest
            {
                RealmId = realmId,
                ContainerId = settlementId,
                IdempotencyKey = $"{request.IdempotencyKey}:settlement_escrow",
                ActorType = "marketplace",
                ActorId = request.OrderId
            }, ct);

            await _escrow.MakeAssetsClaimableAsync(new EscrowActionRequest
            {
                RealmId = realmId,
                ContainerId = settlementId,
                IdempotencyKey = $"{request.IdempotencyKey}:settlement_claimable",
                ActorType = "marketplace",
                ActorId = request.OrderId
            }, ct);

            var payoutAsset = settlementAdd.Assets.FirstOrDefault(a =>
                a.Metadata.TryGetValue("assetRole", out var role) && string.Equals(role, "seller_payout", StringComparison.OrdinalIgnoreCase));
            var taxAsset = settlementAdd.Assets.FirstOrDefault(a =>
                a.Metadata.TryGetValue("assetRole", out var role) && string.Equals(role, "tax", StringComparison.OrdinalIgnoreCase));

            if (payoutAsset == null)
                throw new InvalidOperationException("Settlement payout asset was not created.");

            await _escrow.ReleaseAssetsAsync(new EscrowActionRequest
            {
                RealmId = realmId,
                ContainerId = settlementId,
                IdempotencyKey = $"{request.IdempotencyKey}:settlement_release",
                ActorType = "marketplace",
                ActorId = request.OrderId,
                AssetIds = [payoutAsset.Id],
                TargetAccountId = order.OwnerAccountId,
                TargetCharacterId = order.OwnerCharacterId,
                TargetInventory = request.TargetInventory
            }, ct);

            if (taxAsset != null)
            {
                await _escrow.ExpireEscrowAsync(new EscrowActionRequest
                {
                    RealmId = realmId,
                    ContainerId = settlementId,
                    IdempotencyKey = $"{request.IdempotencyKey}:settlement_tax_expire",
                    ActorType = "marketplace",
                    ActorId = request.OrderId,
                    AssetIds = [taxAsset.Id]
                }, ct);
            }

            await _escrow.ReleaseAssetsAsync(new EscrowActionRequest
            {
                RealmId = realmId,
                ContainerId = order.EscrowContainerId,
                IdempotencyKey = $"{request.IdempotencyKey}:release_item",
                ActorType = "marketplace",
                ActorId = request.OrderId,
                AssetIds = [order.EscrowPrimaryAssetId],
                TargetAccountId = request.FulfillerAccountId,
                TargetCharacterId = request.FulfillerCharacterId,
                TargetInventory = request.TargetInventory
            }, ct);

            tx = BuildTransaction(order, request.FulfillerCharacterId, order.OwnerCharacterId, request.Quantity, tax);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.SellerItemInstanceId))
                throw new InvalidOperationException("SellerItemInstanceId is required to fulfill buy orders.");

            if (string.IsNullOrWhiteSpace(order.EscrowPrimaryAssetId))
                throw new InvalidOperationException("Buy order payout escrow asset is missing.");

            if (!string.IsNullOrWhiteSpace(request.SellerItemDefinitionId) &&
                !string.Equals(order.ItemDefinitionId, request.SellerItemDefinitionId, StringComparison.Ordinal))
                throw new InvalidOperationException("Seller item does not match buy order requirements.");

            var transferId = $"escrow_marketplace_transfer_{request.OrderId}_{ShortHash(request.IdempotencyKey)}";

            await _escrow.CreateEscrowContainerAsync(new CreateEscrowContainerRequest
            {
                RealmId = realmId,
                ContainerId = transferId,
                EscrowType = "marketplace_item_transfer",
                SourceFeature = "marketplace",
                SourceEntityType = "order_transfer",
                SourceEntityId = request.OrderId,
                ResolutionPolicy = EscrowResolutionPolicy.ReturnToSource,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["orderId"] = request.OrderId,
                    ["flow"] = "buy"
                }
            }, ct);

            var transferAdd = await _escrow.AddAssetsAsync(new AddEscrowAssetsRequest
            {
                RealmId = realmId,
                ContainerId = transferId,
                IdempotencyKey = $"{request.IdempotencyKey}:transfer_add",
                ActorType = "marketplace",
                ActorId = request.OrderId,
                Assets =
                [
                    new EscrowAssetDraft
                    {
                        AssetType = EscrowAssetType.ItemInstance,
                        HoldingMode = EscrowHoldingMode.Escrowed,
                        SubtypeKey = order.ItemDefinitionId,
                        QuantityValue = request.Quantity,
                        ItemInstanceId = request.SellerItemInstanceId,
                        SourceAccountId = request.FulfillerAccountId,
                        SourceCharacterId = request.FulfillerCharacterId,
                        SourceInventory = request.SourceInventory,
                        RecipientAccountId = order.OwnerAccountId,
                        RecipientCharacterId = order.OwnerCharacterId,
                        RecipientInventory = request.TargetInventory,
                        Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["orderId"] = request.OrderId
                        }
                    }
                ]
            }, ct);

            await _escrow.EscrowAssetsAsync(new EscrowActionRequest
            {
                RealmId = realmId,
                ContainerId = transferId,
                IdempotencyKey = $"{request.IdempotencyKey}:transfer_escrow",
                ActorType = "marketplace",
                ActorId = request.OrderId
            }, ct);

            await _escrow.MakeAssetsClaimableAsync(new EscrowActionRequest
            {
                RealmId = realmId,
                ContainerId = transferId,
                IdempotencyKey = $"{request.IdempotencyKey}:transfer_claimable",
                ActorType = "marketplace",
                ActorId = request.OrderId
            }, ct);

            var transferAssetId = transferAdd.Assets.FirstOrDefault()?.Id;
            if (string.IsNullOrWhiteSpace(transferAssetId))
                throw new InvalidOperationException("Transfer asset id is missing.");

            await _escrow.ReleaseAssetsAsync(new EscrowActionRequest
            {
                RealmId = realmId,
                ContainerId = transferId,
                IdempotencyKey = $"{request.IdempotencyKey}:transfer_release",
                ActorType = "marketplace",
                ActorId = request.OrderId,
                AssetIds = [transferAssetId],
                TargetAccountId = order.OwnerAccountId,
                TargetCharacterId = order.OwnerCharacterId,
                TargetInventory = request.TargetInventory
            }, ct);

            await _escrow.ReleaseAssetsAsync(new EscrowActionRequest
            {
                RealmId = realmId,
                ContainerId = order.EscrowContainerId,
                IdempotencyKey = $"{request.IdempotencyKey}:release_payout",
                ActorType = "marketplace",
                ActorId = request.OrderId,
                AssetIds = [order.EscrowPrimaryAssetId],
                TargetAccountId = request.FulfillerAccountId,
                TargetCharacterId = request.FulfillerCharacterId,
                TargetInventory = request.TargetInventory
            }, ct);

            if (!string.IsNullOrWhiteSpace(order.EscrowTaxAssetId))
            {
                await _escrow.ExpireEscrowAsync(new EscrowActionRequest
                {
                    RealmId = realmId,
                    ContainerId = order.EscrowContainerId,
                    IdempotencyKey = $"{request.IdempotencyKey}:buy_tax_expire",
                    ActorType = "marketplace",
                    ActorId = request.OrderId,
                    AssetIds = [order.EscrowTaxAssetId]
                }, ct);
            }

            tx = BuildTransaction(order, order.OwnerCharacterId, request.FulfillerCharacterId, request.Quantity, tax);
        }

        var updated = await MutateOrderAsync(realmId, order.OrderId, request.IdempotencyKey, orderRecord =>
        {
            orderRecord.QuantityRemaining = Math.Max(0, orderRecord.QuantityRemaining - request.Quantity);
            orderRecord.UpdatedAtUtc = DateTime.UtcNow;

            if (orderRecord.QuantityRemaining <= 0)
            {
                orderRecord.Status = MarketplaceOrderStatus.Fulfilled;
                orderRecord.FulfilledAtUtc = DateTime.UtcNow;
            }
            else
            {
                orderRecord.Status = MarketplaceOrderStatus.PartiallyFilled;
            }
        }, ct);

        if (updated.Status == MarketplaceOrderStatus.Fulfilled)
        {
            await InvalidatePendingOffersForOrderAsync(
                updated,
                $"{request.IdempotencyKey}:invalidate_offers",
                MarketplaceOfferResolutionType.OrderResolvedElsewhere,
                "Order fulfilled by standard marketplace fulfillment.",
                request.FulfillerCharacterId,
                ct);
        }

        await _repo.TryCreateTransactionAsync(tx, ct);

        return ToSnapshot(updated, quantityOverride: updated.QuantityRemaining);
    }

    public async Task<MarketplaceOrderSnapshot> CancelOrderAsync(CancelMarketplaceOrderRequest request, CancellationToken ct = default)
    {
        ValidateIdempotency(request.IdempotencyKey);
        if (string.IsNullOrWhiteSpace(request.OrderId)) throw new InvalidOperationException("OrderId is required.");

        var realmId = NormalizeRealm(request.RealmId);
        var lockKey = $"{realmId}:{request.OrderId}";

        using var _ = await AcquireLockAsync(lockKey, ct);

        var order = await _repo.GetOrderAsync(realmId, request.OrderId, ct)
            ?? throw new InvalidOperationException("Order was not found.");

        if (!request.IsAdminOverride && !string.Equals(order.OwnerCharacterId, request.ActorCharacterId, StringComparison.Ordinal))
            throw new InvalidOperationException("Only the owner can cancel this order.");

        if (IsTerminal(order.Status))
            return ToSnapshot(order, quantityOverride: order.QuantityRemaining);

        await _escrow.ReturnAssetsAsync(new EscrowActionRequest
        {
            RealmId = realmId,
            ContainerId = order.EscrowContainerId,
            IdempotencyKey = $"{request.IdempotencyKey}:return_assets",
            ActorType = request.IsAdminOverride ? "admin" : "marketplace",
            ActorId = request.IsAdminOverride ? "admin_marketplace" : request.ActorCharacterId
        }, ct);

        var updated = await MutateOrderAsync(realmId, order.OrderId, request.IdempotencyKey, orderRecord =>
        {
            orderRecord.Status = MarketplaceOrderStatus.Cancelled;
            orderRecord.CancelledAtUtc = DateTime.UtcNow;
            orderRecord.UpdatedAtUtc = DateTime.UtcNow;
        }, ct);

        await InvalidatePendingOffersForOrderAsync(
            updated,
            $"{request.IdempotencyKey}:invalidate_offers",
            MarketplaceOfferResolutionType.OrderCancelled,
            "Order cancelled by owner or admin.",
            request.ActorCharacterId,
            ct);

        return ToSnapshot(updated, quantityOverride: updated.QuantityRemaining);
    }

    public async Task<MarketplaceOfferSnapshot> CreateOfferAsync(CreateMarketplaceOfferRequest request, CancellationToken ct = default)
    {
        ValidateIdempotency(request.IdempotencyKey);
        if (string.IsNullOrWhiteSpace(request.MarketplaceOrderId)) throw new InvalidOperationException("MarketplaceOrderId is required.");
        if (string.IsNullOrWhiteSpace(request.OfferCreatorAccountId)) throw new InvalidOperationException("OfferCreatorAccountId is required.");
        if (string.IsNullOrWhiteSpace(request.OfferCreatorCharacterId)) throw new InvalidOperationException("OfferCreatorCharacterId is required.");

        var hasItemAssets = request.OfferedAssets != null && request.OfferedAssets.Count > 0;
        var hasGold = request.OfferedGold > 0;
        if (!hasItemAssets && !hasGold)
            throw new InvalidOperationException("Offer must include item assets and/or offered gold.");

        if (hasGold && !_options.AllowOfferGoldMix)
            throw new InvalidOperationException("Gold in offers is disabled by Marketplace configuration.");

        if (hasGold && string.IsNullOrWhiteSpace(request.OfferedGoldItemInstanceId))
            throw new InvalidOperationException("OfferedGoldItemInstanceId is required when offering gold.");

        var realmId = NormalizeRealm(request.RealmId);
        var lockKey = $"{realmId}:{request.MarketplaceOrderId}";

        using var _ = await AcquireLockAsync(lockKey, ct);

        var order = await _repo.GetOrderAsync(realmId, request.MarketplaceOrderId, ct)
            ?? throw new InvalidOperationException("Marketplace order was not found.");

        ValidateOrderAllowsOffers(order);

        if (string.Equals(order.OwnerCharacterId, request.OfferCreatorCharacterId, StringComparison.Ordinal))
            throw new InvalidOperationException("Cannot create offer on your own marketplace order.");

        if (DateTime.UtcNow >= order.ExpiresAtUtc)
            throw new InvalidOperationException("Marketplace order has expired.");

        await EnsureActiveOfferLimitsAsync(realmId, order.OrderId, request.OfferCreatorCharacterId, ct);
        EnsureNoDuplicateOfferItemInstances(request);
        await EnsureOfferItemsNotInPendingOffersAsync(realmId, request.OfferCreatorCharacterId, request.OfferedAssets ?? [], ct);

        var offerId = MakeStableId("mkt_offer", request.IdempotencyKey, request.OfferCreatorCharacterId, order.OrderId);
        var existing = await _repo.GetOfferAsync(realmId, offerId, ct);
        if (existing != null)
            return ToOfferSnapshot(existing);

        var durationHours = ResolveOfferDurationHours(request.DurationHours);
        var now = DateTime.UtcNow;
        var submissionFee = Math.Max(0, _options.OfferSubmissionFeeGold);

        var offer = new MarketplaceOffer
        {
            OfferId = offerId,
            RealmId = realmId,
            IdempotencyKey = request.IdempotencyKey,
            MarketplaceOrderId = order.OrderId,
            OfferCreatorAccountId = request.OfferCreatorAccountId,
            OfferCreatorCharacterId = request.OfferCreatorCharacterId,
            OrderOwnerCharacterId = order.OwnerCharacterId,
            Status = MarketplaceOfferStatus.Pending,
            EscrowContainerId = $"escrow_marketplace_offer_{offerId}",
            OfferedGold = Math.Max(0, request.OfferedGold),
            SubmissionFeeGold = submissionFee,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(durationHours),
            Version = 1
        };

        if (!string.IsNullOrWhiteSpace(request.OfferMessage))
            offer.Metadata["offerMessage"] = request.OfferMessage.Trim();

        if (submissionFee > 0)
        {
            if (string.IsNullOrWhiteSpace(request.OfferedGoldItemInstanceId))
                throw new InvalidOperationException("OfferedGoldItemInstanceId is required when offer submission fee is enabled.");

            await SinkGoldAsync(
                realmId,
                request.OfferCreatorAccountId,
                request.OfferCreatorCharacterId,
                request.OfferedGoldItemInstanceId,
                submissionFee,
                string.IsNullOrWhiteSpace(request.SourceInventory) ? "inventory" : request.SourceInventory,
                request.IdempotencyKey,
                offer.OfferId,
                ct);
        }

        await _escrow.CreateEscrowContainerAsync(new CreateEscrowContainerRequest
        {
            RealmId = realmId,
            ContainerId = offer.EscrowContainerId,
            EscrowType = "marketplace_offer",
            SourceFeature = "marketplace",
            SourceEntityType = "offer",
            SourceEntityId = offerId,
            ExpiresUtc = offer.ExpiresAtUtc,
            ResolutionPolicy = EscrowResolutionPolicy.ReturnToSource,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["offerId"] = offerId,
                ["orderId"] = order.OrderId
            }
        }, ct);

        var drafts = BuildOfferEscrowDrafts(request, offer, order);

        var addResult = await _escrow.AddAssetsAsync(new AddEscrowAssetsRequest
        {
            RealmId = realmId,
            ContainerId = offer.EscrowContainerId,
            IdempotencyKey = $"{request.IdempotencyKey}:offer_add",
            ActorType = "marketplace",
            ActorId = offer.OfferId,
            Assets = drafts
        }, ct);

        await _escrow.EscrowAssetsAsync(new EscrowActionRequest
        {
            RealmId = realmId,
            ContainerId = offer.EscrowContainerId,
            IdempotencyKey = $"{request.IdempotencyKey}:offer_escrow",
            ActorType = "marketplace",
            ActorId = offer.OfferId
        }, ct);

        await _escrow.MakeAssetsClaimableAsync(new EscrowActionRequest
        {
            RealmId = realmId,
            ContainerId = offer.EscrowContainerId,
            IdempotencyKey = $"{request.IdempotencyKey}:offer_claimable",
            ActorType = "marketplace",
            ActorId = offer.OfferId
        }, ct);

        offer.OfferedAssets = BuildOfferAssetsFromEscrow(addResult, offer, request.OfferedAssets ?? []);

        var goldAsset = addResult.Assets.FirstOrDefault(x => x.Metadata.TryGetValue("assetRole", out var role) && string.Equals(role, "offered_gold", StringComparison.OrdinalIgnoreCase));
        if (goldAsset != null)
            offer.OfferedGoldEscrowAssetId = goldAsset.Id;

        if (!await _repo.TryCreateOfferAsync(offer, ct))
        {
            var raced = await _repo.GetOfferAsync(realmId, offerId, ct)
                ?? throw new InvalidOperationException("Failed to create marketplace offer.");
            return ToOfferSnapshot(raced);
        }

        await MutateOrderAsync(realmId, order.OrderId, $"{request.IdempotencyKey}:order_offer_count", orderRecord =>
        {
            orderRecord.PendingOfferCount = Math.Max(0, orderRecord.PendingOfferCount) + 1;
        }, ct);

        _logger.LogInformation(
            "Marketplace offer submitted. realm={RealmId} order={OrderId} offer={OfferId} creator={Creator} itemCount={ItemCount} offeredGold={OfferedGold}",
            realmId,
            order.OrderId,
            offer.OfferId,
            offer.OfferCreatorCharacterId,
            offer.OfferedAssets.Count,
            offer.OfferedGold);

        return ToOfferSnapshot(offer);
    }

    public async Task<MarketplaceOfferSnapshot> RespondOfferAsync(RespondMarketplaceOfferRequest request, CancellationToken ct = default)
    {
        ValidateIdempotency(request.IdempotencyKey);
        if (string.IsNullOrWhiteSpace(request.OfferId)) throw new InvalidOperationException("OfferId is required.");
        if (string.IsNullOrWhiteSpace(request.ResponseCharacterId)) throw new InvalidOperationException("ResponseCharacterId is required.");

        var realmId = NormalizeRealm(request.RealmId);
        var offer = await _repo.GetOfferAsync(realmId, request.OfferId, ct)
            ?? throw new InvalidOperationException("Marketplace offer was not found.");

        var lockKey = $"{realmId}:{offer.MarketplaceOrderId}";
        using var _ = await AcquireLockAsync(lockKey, ct);

        offer = await _repo.GetOfferAsync(realmId, request.OfferId, ct)
            ?? throw new InvalidOperationException("Marketplace offer was not found.");

        var order = await _repo.GetOrderAsync(realmId, offer.MarketplaceOrderId, ct)
            ?? throw new InvalidOperationException("Marketplace order was not found.");

        if (!string.Equals(order.OwnerCharacterId, request.ResponseCharacterId, StringComparison.Ordinal))
            throw new InvalidOperationException("Only the order owner can respond to offers.");

        if (offer.Status != MarketplaceOfferStatus.Pending)
            return ToOfferSnapshot(offer);

        if (DateTime.UtcNow >= offer.ExpiresAtUtc)
        {
            var expired = await ExpireOfferInternalAsync(offer, $"{request.IdempotencyKey}:auto_expire", request.ResponseCharacterId, ct);
            return ToOfferSnapshot(expired);
        }

        if (IsTerminal(order.Status))
            throw new InvalidOperationException($"Order is already {order.Status}. Offer can no longer be accepted.");

        if (!request.Accept)
        {
            var rejected = await ResolveOfferAndReturnAssetsAsync(
                offer,
                request.IdempotencyKey,
                MarketplaceOfferStatus.Rejected,
                MarketplaceOfferResolutionType.ManualReject,
                request.Summary ?? "Offer rejected by order owner.",
                request.ResponseCharacterId,
                decrementPendingOrderCount: true,
                ct);

            _logger.LogInformation(
                "Marketplace offer rejected. realm={RealmId} order={OrderId} offer={OfferId} owner={Owner}",
                realmId,
                order.OrderId,
                offer.OfferId,
                request.ResponseCharacterId);

            return ToOfferSnapshot(rejected);
        }

        if (string.IsNullOrWhiteSpace(order.EscrowPrimaryAssetId))
            throw new InvalidOperationException("Order escrow primary asset is missing.");

        var offeredAssetIds = offer.OfferedAssets
            .Select(x => x.EscrowAssetId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(offer.OfferedGoldEscrowAssetId))
            offeredAssetIds.Add(offer.OfferedGoldEscrowAssetId);

        if (offeredAssetIds.Count == 0)
            throw new InvalidOperationException("Offer has no escrowed assets to settle.");

        await _escrow.ReleaseAssetsAsync(new EscrowActionRequest
        {
            RealmId = realmId,
            ContainerId = order.EscrowContainerId,
            IdempotencyKey = $"{request.IdempotencyKey}:accept_release_order_asset",
            ActorType = "marketplace",
            ActorId = order.OrderId,
            AssetIds = [order.EscrowPrimaryAssetId],
            TargetAccountId = offer.OfferCreatorAccountId,
            TargetCharacterId = offer.OfferCreatorCharacterId,
            TargetInventory = "inventory"
        }, ct);

        await _escrow.ReleaseAssetsAsync(new EscrowActionRequest
        {
            RealmId = realmId,
            ContainerId = offer.EscrowContainerId,
            IdempotencyKey = $"{request.IdempotencyKey}:accept_release_offer_assets",
            ActorType = "marketplace",
            ActorId = offer.OfferId,
            AssetIds = offeredAssetIds,
            TargetAccountId = order.OwnerAccountId,
            TargetCharacterId = order.OwnerCharacterId,
            TargetInventory = "inventory"
        }, ct);

        var acceptedOffer = await MutateOfferAsync(realmId, offer.OfferId, request.IdempotencyKey, record =>
        {
            record.Status = MarketplaceOfferStatus.Accepted;
            record.ResponseCharacterId = request.ResponseCharacterId;
            record.AcceptedAtUtc = DateTime.UtcNow;
            record.RespondedAtUtc = record.AcceptedAtUtc;
            record.Resolution = new MarketplaceOfferResolution
            {
                ResolutionType = MarketplaceOfferResolutionType.ManualAccept,
                Summary = string.IsNullOrWhiteSpace(request.Summary) ? "Offer accepted by order owner." : request.Summary,
                ResolvedAtUtc = DateTime.UtcNow,
                ResolvedByCharacterId = request.ResponseCharacterId
            };
        }, ct);

        var updatedOrder = await MutateOrderAsync(realmId, order.OrderId, $"{request.IdempotencyKey}:accept_order", orderRecord =>
        {
            orderRecord.QuantityRemaining = 0;
            orderRecord.Status = MarketplaceOrderStatus.Fulfilled;
            orderRecord.FulfilledAtUtc = DateTime.UtcNow;
            orderRecord.PendingOfferCount = 0;
        }, ct);

        await InvalidatePendingOffersForOrderAsync(
            updatedOrder,
            $"{request.IdempotencyKey}:competing_offers",
            MarketplaceOfferResolutionType.OrderResolvedElsewhere,
            "Order fulfilled by accepted marketplace offer.",
            request.ResponseCharacterId,
            ct,
            skipOfferId: acceptedOffer.OfferId);

        var tx = new MarketplaceTransaction
        {
            TransactionId = $"mkt_tx_{Guid.NewGuid():N}",
            RealmId = updatedOrder.RealmId,
            OrderId = updatedOrder.OrderId,
            OrderType = updatedOrder.OrderType,
            BuyerCharacterId = offer.OfferCreatorCharacterId,
            SellerCharacterId = order.OwnerCharacterId,
            ItemDefinitionId = updatedOrder.ItemDefinitionId,
            Quantity = Math.Max(1, updatedOrder.QuantityTotal),
            UnitPriceGold = 0,
            GrossGold = offer.OfferedGold,
            TaxGold = 0,
            NetSettlementGold = offer.OfferedGold,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _repo.TryCreateTransactionAsync(tx, ct);

        _logger.LogInformation(
            "Marketplace offer accepted. realm={RealmId} order={OrderId} offer={OfferId} owner={Owner} creator={Creator}",
            realmId,
            updatedOrder.OrderId,
            acceptedOffer.OfferId,
            order.OwnerCharacterId,
            offer.OfferCreatorCharacterId);

        return ToOfferSnapshot(acceptedOffer);
    }

    public async Task<MarketplaceOfferSnapshot> CancelOfferAsync(CancelMarketplaceOfferRequest request, CancellationToken ct = default)
    {
        ValidateIdempotency(request.IdempotencyKey);
        if (string.IsNullOrWhiteSpace(request.OfferId)) throw new InvalidOperationException("OfferId is required.");
        if (string.IsNullOrWhiteSpace(request.ActorCharacterId)) throw new InvalidOperationException("ActorCharacterId is required.");

        var realmId = NormalizeRealm(request.RealmId);
        var offer = await _repo.GetOfferAsync(realmId, request.OfferId, ct)
            ?? throw new InvalidOperationException("Marketplace offer was not found.");

        var lockKey = $"{realmId}:{offer.MarketplaceOrderId}";
        using var _ = await AcquireLockAsync(lockKey, ct);

        offer = await _repo.GetOfferAsync(realmId, request.OfferId, ct)
            ?? throw new InvalidOperationException("Marketplace offer was not found.");

        if (!string.Equals(offer.OfferCreatorCharacterId, request.ActorCharacterId, StringComparison.Ordinal))
            throw new InvalidOperationException("Only the offer creator can cancel this offer.");

        if (offer.Status != MarketplaceOfferStatus.Pending)
            return ToOfferSnapshot(offer);

        var cancelled = await ResolveOfferAndReturnAssetsAsync(
            offer,
            request.IdempotencyKey,
            MarketplaceOfferStatus.Cancelled,
            MarketplaceOfferResolutionType.CreatorCancelled,
            request.Summary ?? "Offer cancelled by creator.",
            request.ActorCharacterId,
            decrementPendingOrderCount: true,
            ct);

        _logger.LogInformation(
            "Marketplace offer cancelled. realm={RealmId} order={OrderId} offer={OfferId} creator={Creator}",
            realmId,
            offer.MarketplaceOrderId,
            cancelled.OfferId,
            request.ActorCharacterId);

        return ToOfferSnapshot(cancelled);
    }

    public async Task<MarketplaceBrowseResult> BrowseAsync(MarketplaceBrowseQuery query, CancellationToken ct = default)
    {
        query ??= new MarketplaceBrowseQuery();

        var realmId = NormalizeRealm(query.RealmId);
        var all = await _repo.ListAllOrdersAsync(realmId, ct);

        IEnumerable<MarketplaceOrder> q = all;

        if (query.OrderType.HasValue)
            q = q.Where(x => x.OrderType == query.OrderType.Value);

        if (query.Status.HasValue)
            q = q.Where(x => x.Status == query.Status.Value);

        if (!string.IsNullOrWhiteSpace(query.ItemDefinitionId))
            q = q.Where(x => string.Equals(x.ItemDefinitionId, query.ItemDefinitionId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.CharacterId))
            q = q.Where(x => string.Equals(x.OwnerCharacterId, query.CharacterId, StringComparison.Ordinal));

        q = q.Where(x => !IsTerminal(x.Status));

        q = query.Sort.Trim().ToLowerInvariant() switch
        {
            "price_asc" => q.OrderBy(x => x.UnitPriceGold).ThenByDescending(x => x.CreatedAtUtc),
            "price_desc" => q.OrderByDescending(x => x.UnitPriceGold).ThenByDescending(x => x.CreatedAtUtc),
            _ => q.OrderByDescending(x => x.CreatedAtUtc)
        };

        var total = q.Count();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var items = q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(order => ToSnapshot(order, quantityOverride: order.QuantityRemaining))
            .ToList();

        return new MarketplaceBrowseResult
        {
            Total = total,
            Items = items
        };
    }

    public async Task<MarketplaceBrowseResult> GetMyOrdersAsync(string realmId, string ownerCharacterId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerCharacterId))
            throw new InvalidOperationException("ownerCharacterId is required.");

        var records = await _repo.ListOrdersByOwnerAsync(NormalizeRealm(realmId), ownerCharacterId, ct);

        var total = records.Count;
        var items = records
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 200))
            .Take(Math.Clamp(pageSize, 1, 200))
            .Select(x => ToSnapshot(x, quantityOverride: x.QuantityRemaining))
            .ToList();

        return new MarketplaceBrowseResult
        {
            Total = total,
            Items = items
        };
    }

    public async Task<MarketplaceOfferBrowseResult> GetOffersByOrderAsync(string realmId, string orderId, bool includeHistory = false, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            throw new InvalidOperationException("orderId is required.");

        var offers = await _repo.ListOffersByOrderAsync(NormalizeRealm(realmId), orderId, ct);
        IEnumerable<MarketplaceOffer> q = offers;

        if (!includeHistory)
            q = q.Where(x => x.Status == MarketplaceOfferStatus.Pending);

        var total = q.Count();
        var items = q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 200))
            .Take(Math.Clamp(pageSize, 1, 200))
            .Select(ToOfferSnapshot)
            .ToList();

        return new MarketplaceOfferBrowseResult
        {
            Total = total,
            Items = items
        };
    }

    public async Task<MarketplaceOfferBrowseResult> GetMyOffersAsync(string realmId, string creatorCharacterId, bool includeHistory = true, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(creatorCharacterId))
            throw new InvalidOperationException("creatorCharacterId is required.");

        var offers = await _repo.ListOffersByCreatorAsync(NormalizeRealm(realmId), creatorCharacterId, ct);
        IEnumerable<MarketplaceOffer> q = offers;

        if (!includeHistory)
            q = q.Where(x => x.Status == MarketplaceOfferStatus.Pending);

        var total = q.Count();
        var items = q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 200))
            .Take(Math.Clamp(pageSize, 1, 200))
            .Select(ToOfferSnapshot)
            .ToList();

        return new MarketplaceOfferBrowseResult
        {
            Total = total,
            Items = items
        };
    }

    public async Task<MarketplaceAdminSearchResult> SearchAdminAsync(MarketplaceAdminSearchQuery query, CancellationToken ct = default)
    {
        query ??= new MarketplaceAdminSearchQuery();

        var realmId = NormalizeRealm(query.RealmId);
        var all = await _repo.ListAllOrdersAsync(realmId, ct);

        IEnumerable<MarketplaceOrder> q = all;

        if (query.OrderType.HasValue)
            q = q.Where(x => x.OrderType == query.OrderType.Value);

        if (query.Status.HasValue)
            q = q.Where(x => x.Status == query.Status.Value);

        if (!string.IsNullOrWhiteSpace(query.CharacterId))
            q = q.Where(x => string.Equals(x.OwnerCharacterId, query.CharacterId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(query.ItemDefinitionId))
            q = q.Where(x => string.Equals(x.ItemDefinitionId, query.ItemDefinitionId, StringComparison.OrdinalIgnoreCase));

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
            .Select(x => ToSnapshot(x, quantityOverride: x.QuantityRemaining))
            .ToList();

        return new MarketplaceAdminSearchResult
        {
            Total = total,
            Items = items
        };
    }

    public async Task<MarketplaceAdminDetail?> GetAdminDetailAsync(string realmId, string orderId, CancellationToken ct = default)
    {
        var order = await _repo.GetOrderAsync(NormalizeRealm(realmId), orderId, ct);
        if (order == null) return null;

        EscrowContainer? container = null;
        EscrowSummary? summary = null;

        if (!string.IsNullOrWhiteSpace(order.EscrowContainerId))
        {
            container = await _escrow.GetEscrowContainerAsync(order.RealmId, order.EscrowContainerId, ct);
            summary = await _escrow.GetEscrowSummaryAsync(order.RealmId, order.EscrowContainerId, ct);
        }

        var tx = await _repo.ListTransactionsByOrderAsync(order.RealmId, order.OrderId, ct);

        return new MarketplaceAdminDetail
        {
            Order = ToSnapshot(order, quantityOverride: order.QuantityRemaining),
            EscrowContainer = container,
            EscrowSummary = summary,
            Transactions = tx
        };
    }

    public async Task<MarketplaceOfferAdminSearchResult> SearchOffersAdminAsync(MarketplaceOfferAdminSearchQuery query, CancellationToken ct = default)
    {
        query ??= new MarketplaceOfferAdminSearchQuery();

        var realmId = NormalizeRealm(query.RealmId);
        var all = await _repo.ListAllOffersAsync(realmId, ct);

        IEnumerable<MarketplaceOffer> q = all;

        if (query.Status.HasValue)
            q = q.Where(x => x.Status == query.Status.Value);

        if (!string.IsNullOrWhiteSpace(query.MarketplaceOrderId))
            q = q.Where(x => string.Equals(x.MarketplaceOrderId, query.MarketplaceOrderId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(query.CharacterId))
            q = q.Where(x =>
                string.Equals(x.OfferCreatorCharacterId, query.CharacterId, StringComparison.Ordinal) ||
                string.Equals(x.OrderOwnerCharacterId, query.CharacterId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(query.ItemDefinitionId))
            q = q.Where(x => x.OfferedAssets.Any(a => string.Equals(a.ItemDefinitionId, query.ItemDefinitionId, StringComparison.OrdinalIgnoreCase)));

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
            .Select(ToOfferSnapshot)
            .ToList();

        return new MarketplaceOfferAdminSearchResult
        {
            Total = total,
            Items = items
        };
    }

    public async Task<MarketplaceOfferAdminDetail?> GetOfferAdminDetailAsync(string realmId, string offerId, CancellationToken ct = default)
    {
        var offer = await _repo.GetOfferAsync(NormalizeRealm(realmId), offerId, ct);
        if (offer == null) return null;

        var order = await _repo.GetOrderAsync(offer.RealmId, offer.MarketplaceOrderId, ct);

        EscrowContainer? container = null;
        EscrowSummary? summary = null;

        if (!string.IsNullOrWhiteSpace(offer.EscrowContainerId))
        {
            container = await _escrow.GetEscrowContainerAsync(offer.RealmId, offer.EscrowContainerId, ct);
            summary = await _escrow.GetEscrowSummaryAsync(offer.RealmId, offer.EscrowContainerId, ct);
        }

        return new MarketplaceOfferAdminDetail
        {
            Offer = ToOfferSnapshot(offer),
            Order = order == null ? null : ToSnapshot(order, quantityOverride: order.QuantityRemaining),
            EscrowContainer = container,
            EscrowSummary = summary
        };
    }

    private async Task EnsureActiveOrderLimitAsync(string realmId, string ownerCharacterId, CancellationToken ct)
    {
        var ownerOrders = await _repo.ListOrdersByOwnerAsync(realmId, ownerCharacterId, ct);
        var activeCount = ownerOrders.Count(x => x.Status == MarketplaceOrderStatus.Active || x.Status == MarketplaceOrderStatus.PartiallyFilled);

        if (activeCount >= Math.Max(1, _options.MaxActiveOrdersPerCharacter))
            throw new InvalidOperationException("Max active marketplace orders reached for this character.");
    }

    private async Task EnsureActiveOfferLimitsAsync(string realmId, string orderId, string creatorCharacterId, CancellationToken ct)
    {
        var pendingOnOrder = (await _repo.ListOffersByOrderAsync(realmId, orderId, ct)).Count(x => x.Status == MarketplaceOfferStatus.Pending);
        if (pendingOnOrder >= Math.Max(1, _options.MaxPendingOffersPerOrder))
            throw new InvalidOperationException("Max pending offers reached for this order.");

        var pendingByCreator = (await _repo.ListOffersByCreatorAsync(realmId, creatorCharacterId, ct)).Count(x => x.Status == MarketplaceOfferStatus.Pending);
        if (pendingByCreator >= Math.Max(1, _options.MaxActiveOffersPerCharacter))
            throw new InvalidOperationException("Max active offers reached for this character.");
    }

    private static void EnsureNoDuplicateOfferItemInstances(CreateMarketplaceOfferRequest request)
    {
        if (request.OfferedAssets == null || request.OfferedAssets.Count == 0)
            return;

        var dup = request.OfferedAssets
            .Where(x => !string.IsNullOrWhiteSpace(x.ItemInstanceId))
            .GroupBy(x => x.ItemInstanceId, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);

        if (dup != null)
            throw new InvalidOperationException("An item instance cannot appear multiple times in the same offer.");
    }

    private async Task EnsureOfferItemsNotInPendingOffersAsync(
        string realmId,
        string creatorCharacterId,
        IReadOnlyList<CreateMarketplaceOfferAssetRequest> offeredAssets,
        CancellationToken ct)
    {
        if (offeredAssets.Count == 0)
            return;

        var offeredIds = offeredAssets
            .Where(x => !string.IsNullOrWhiteSpace(x.ItemInstanceId))
            .Select(x => x.ItemInstanceId)
            .ToHashSet(StringComparer.Ordinal);

        if (offeredIds.Count == 0)
            return;

        var existingPending = (await _repo.ListOffersByCreatorAsync(realmId, creatorCharacterId, ct))
            .Where(x => x.Status == MarketplaceOfferStatus.Pending)
            .ToList();

        foreach (var pending in existingPending)
        {
            if (pending.OfferedAssets.Any(a => offeredIds.Contains(a.ItemInstanceId)))
                throw new InvalidOperationException("One or more offered item instances are already escrowed in another pending offer.");
        }
    }

    private void ValidateOrderAllowsOffers(MarketplaceOrder order)
    {
        if (IsTerminal(order.Status))
            throw new InvalidOperationException($"Order is already {order.Status}.");

        if (order.OrderType == MarketplaceOrderType.Sell && !_options.EnableOffersOnSellOrders)
            throw new InvalidOperationException("Offers are disabled for sell orders.");

        if (order.OrderType == MarketplaceOrderType.Buy && !_options.EnableOffersOnBuyOrders)
            throw new InvalidOperationException("Offers on buy orders are not enabled in this phase.");
    }

    private List<EscrowAssetDraft> BuildOfferEscrowDrafts(CreateMarketplaceOfferRequest request, MarketplaceOffer offer, MarketplaceOrder order)
    {
        var drafts = new List<EscrowAssetDraft>();

        if (request.OfferedAssets != null)
        {
            foreach (var asset in request.OfferedAssets)
            {
                if (string.IsNullOrWhiteSpace(asset.ItemInstanceId))
                    throw new InvalidOperationException("Offer asset ItemInstanceId is required.");
                if (string.IsNullOrWhiteSpace(asset.ItemDefinitionId))
                    throw new InvalidOperationException("Offer asset ItemDefinitionId is required.");
                if (asset.Quantity <= 0)
                    throw new InvalidOperationException("Offer asset quantity must be greater than zero.");
                if (asset.Quantity != 1)
                    throw new InvalidOperationException("V2 offer flow currently requires quantity=1 for item instance escrow safety.");

                drafts.Add(new EscrowAssetDraft
                {
                    AssetType = EscrowAssetType.ItemInstance,
                    HoldingMode = EscrowHoldingMode.Escrowed,
                    SubtypeKey = asset.ItemDefinitionId,
                    QuantityValue = asset.Quantity,
                    ItemInstanceId = asset.ItemInstanceId,
                    SourceAccountId = request.OfferCreatorAccountId,
                    SourceCharacterId = request.OfferCreatorCharacterId,
                    SourceInventory = string.IsNullOrWhiteSpace(request.SourceInventory) ? "inventory" : request.SourceInventory,
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["offerId"] = offer.OfferId,
                        ["orderId"] = order.OrderId,
                        ["assetRole"] = "offered_item",
                        ["itemDefinitionId"] = asset.ItemDefinitionId
                    }
                });
            }
        }

        if (request.OfferedGold > 0)
        {
            drafts.Add(new EscrowAssetDraft
            {
                AssetType = EscrowAssetType.ItemInstance,
                HoldingMode = EscrowHoldingMode.Escrowed,
                SubtypeKey = _options.GoldItemDefinitionId,
                QuantityValue = request.OfferedGold,
                ItemInstanceId = request.OfferedGoldItemInstanceId,
                SourceAccountId = request.OfferCreatorAccountId,
                SourceCharacterId = request.OfferCreatorCharacterId,
                SourceInventory = string.IsNullOrWhiteSpace(request.SourceInventory) ? "inventory" : request.SourceInventory,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["offerId"] = offer.OfferId,
                    ["orderId"] = order.OrderId,
                    ["assetRole"] = "offered_gold"
                }
            });
        }

        return drafts;
    }

    private static List<MarketplaceOfferAsset> BuildOfferAssetsFromEscrow(
        EscrowContainer addResult,
        MarketplaceOffer offer,
        IReadOnlyList<CreateMarketplaceOfferAssetRequest> requestedAssets)
    {
        var offeredItemAssets = addResult.Assets
            .Where(x => x.Metadata.TryGetValue("assetRole", out var role) && string.Equals(role, "offered_item", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var mapped = new List<MarketplaceOfferAsset>(offeredItemAssets.Count);

        foreach (var escrowAsset in offeredItemAssets)
        {
            var itemInstanceId = escrowAsset.ItemInstanceId ?? string.Empty;
            var source = requestedAssets.FirstOrDefault(x => string.Equals(x.ItemInstanceId, itemInstanceId, StringComparison.Ordinal));
            if (source == null)
                continue;

            mapped.Add(new MarketplaceOfferAsset
            {
                OfferAssetId = $"{offer.OfferId}:{source.ItemInstanceId}",
                OfferId = offer.OfferId,
                ItemInstanceId = source.ItemInstanceId,
                ItemDefinitionId = source.ItemDefinitionId,
                Quantity = source.Quantity,
                EscrowAssetId = escrowAsset.Id,
                ItemSnapshot = new Dictionary<string, string>(source.ItemSnapshot ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            });
        }

        return mapped;
    }

    private async Task SinkGoldAsync(
        string realmId,
        string sourceAccountId,
        string sourceCharacterId,
        string sourceGoldItemInstanceId,
        long goldAmount,
        string sourceInventory,
        string idempotencyKey,
        string orderId,
        CancellationToken ct)
    {
        if (goldAmount <= 0)
            return;

        var feeContainerId = $"escrow_marketplace_fee_{orderId}_{ShortHash(idempotencyKey)}";

        await _escrow.CreateEscrowContainerAsync(new CreateEscrowContainerRequest
        {
            RealmId = realmId,
            ContainerId = feeContainerId,
            EscrowType = "marketplace_fee_sink",
            SourceFeature = "marketplace",
            SourceEntityType = "order_fee",
            SourceEntityId = orderId,
            ResolutionPolicy = EscrowResolutionPolicy.DestroyOnExpiry,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["orderId"] = orderId,
                ["purpose"] = "listing_fee"
            }
        }, ct);

        await _escrow.AddAssetsAsync(new AddEscrowAssetsRequest
        {
            RealmId = realmId,
            ContainerId = feeContainerId,
            IdempotencyKey = $"{idempotencyKey}:fee_add",
            ActorType = "marketplace",
            ActorId = orderId,
            Assets =
            [
                new EscrowAssetDraft
                {
                    AssetType = EscrowAssetType.ItemInstance,
                    HoldingMode = EscrowHoldingMode.Escrowed,
                    SubtypeKey = _options.GoldItemDefinitionId,
                    QuantityValue = goldAmount,
                    ItemInstanceId = sourceGoldItemInstanceId,
                    SourceAccountId = sourceAccountId,
                    SourceCharacterId = sourceCharacterId,
                    SourceInventory = sourceInventory,
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["orderId"] = orderId,
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
            ActorType = "marketplace",
            ActorId = orderId
        }, ct);

        await _escrow.ExpireEscrowAsync(new EscrowActionRequest
        {
            RealmId = realmId,
            ContainerId = feeContainerId,
            IdempotencyKey = $"{idempotencyKey}:fee_expire",
            ActorType = "marketplace",
            ActorId = orderId
        }, ct);

        _ = await _escrowRepo.DeleteContainerAsync(realmId, feeContainerId, ct);
    }

    private async Task ExpireOrderInternalAsync(MarketplaceOrder order, string idempotencyKey, CancellationToken ct)
    {
        if (IsTerminal(order.Status))
            return;

        try
        {
            await _escrow.ReturnAssetsAsync(new EscrowActionRequest
            {
                RealmId = order.RealmId,
                ContainerId = order.EscrowContainerId,
                IdempotencyKey = $"{idempotencyKey}:expire_return",
                ActorType = "marketplace",
                ActorId = order.OrderId
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Marketplace expire return failed. realm={RealmId} order={OrderId}", order.RealmId, order.OrderId);
        }

        await MutateOrderAsync(order.RealmId, order.OrderId, $"{idempotencyKey}:expire_state", record =>
        {
            record.Status = MarketplaceOrderStatus.Expired;
            record.UpdatedAtUtc = DateTime.UtcNow;
            record.CancelledAtUtc ??= DateTime.UtcNow;
            record.PendingOfferCount = 0;
        }, ct);

        await InvalidatePendingOffersForOrderAsync(
            order,
            $"{idempotencyKey}:expire_offers",
            MarketplaceOfferResolutionType.OrderExpired,
            "Order expired.",
            "system",
            ct);
    }

    private async Task<MarketplaceOffer> ResolveOfferAndReturnAssetsAsync(
        MarketplaceOffer offer,
        string idempotencyKey,
        MarketplaceOfferStatus terminalStatus,
        MarketplaceOfferResolutionType resolutionType,
        string summary,
        string responseCharacterId,
        bool decrementPendingOrderCount,
        CancellationToken ct)
    {
        await _escrow.ReturnAssetsAsync(new EscrowActionRequest
        {
            RealmId = offer.RealmId,
            ContainerId = offer.EscrowContainerId,
            IdempotencyKey = $"{idempotencyKey}:offer_return",
            ActorType = "marketplace",
            ActorId = offer.OfferId
        }, ct);

        var updated = await MutateOfferAsync(offer.RealmId, offer.OfferId, idempotencyKey, record =>
        {
            record.Status = terminalStatus;
            record.ResponseCharacterId = responseCharacterId;
            record.RespondedAtUtc = DateTime.UtcNow;

            if (terminalStatus == MarketplaceOfferStatus.Rejected)
                record.RejectedAtUtc = DateTime.UtcNow;
            if (terminalStatus == MarketplaceOfferStatus.Cancelled)
                record.CancelledAtUtc = DateTime.UtcNow;
            if (terminalStatus == MarketplaceOfferStatus.Expired)
                record.ExpiredAtUtc = DateTime.UtcNow;

            record.Resolution = new MarketplaceOfferResolution
            {
                ResolutionType = resolutionType,
                Summary = summary,
                ResolvedAtUtc = DateTime.UtcNow,
                ResolvedByCharacterId = responseCharacterId
            };
        }, ct);

        if (decrementPendingOrderCount)
        {
            await MutateOrderAsync(offer.RealmId, offer.MarketplaceOrderId, $"{idempotencyKey}:order_pending_dec", orderRecord =>
            {
                orderRecord.PendingOfferCount = Math.Max(0, orderRecord.PendingOfferCount - 1);
            }, ct);
        }

        return updated;
    }

    private async Task<MarketplaceOffer> ExpireOfferInternalAsync(MarketplaceOffer offer, string idempotencyKey, string actorCharacterId, CancellationToken ct)
    {
        if (offer.Status != MarketplaceOfferStatus.Pending)
            return offer;

        return await ResolveOfferAndReturnAssetsAsync(
            offer,
            idempotencyKey,
            MarketplaceOfferStatus.Expired,
            MarketplaceOfferResolutionType.OfferExpired,
            "Offer expired.",
            actorCharacterId,
            decrementPendingOrderCount: true,
            ct);
    }

    private async Task InvalidatePendingOffersForOrderAsync(
        MarketplaceOrder order,
        string idempotencyKey,
        MarketplaceOfferResolutionType resolutionType,
        string summary,
        string actorCharacterId,
        CancellationToken ct,
        string? skipOfferId = null)
    {
        var offers = await _repo.ListOffersByOrderAsync(order.RealmId, order.OrderId, ct);
        var pending = offers.Where(x => x.Status == MarketplaceOfferStatus.Pending).ToList();

        foreach (var pendingOffer in pending)
        {
            if (!string.IsNullOrWhiteSpace(skipOfferId) && string.Equals(skipOfferId, pendingOffer.OfferId, StringComparison.Ordinal))
                continue;

            await ResolveOfferAndReturnAssetsAsync(
                pendingOffer,
                $"{idempotencyKey}:{pendingOffer.OfferId}",
                MarketplaceOfferStatus.Rejected,
                resolutionType,
                summary,
                actorCharacterId,
                decrementPendingOrderCount: true,
                ct);
        }
    }

    private async Task<MarketplaceOffer> MutateOfferAsync(
        string realmId,
        string offerId,
        string idempotencyKey,
        Action<MarketplaceOffer> mutate,
        CancellationToken ct)
    {
        const int attempts = 5;

        for (var i = 1; i <= attempts; i++)
        {
            var snap = await _repo.GetOfferSnapshotAsync(realmId, offerId, ct)
                ?? throw new InvalidOperationException("Marketplace offer was not found.");

            var record = snap.Record;
            if (record.Metadata.TryGetValue($"op:{idempotencyKey}", out var status) &&
                string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                return record;

            mutate(record);
            record.Metadata[$"op:{idempotencyKey}"] = "completed";
            record.Version++;
            record.UpdatedAtUtc = DateTime.UtcNow;

            var ok = await _repo.TryReplaceOfferAsync(realmId, record, snap.ConcurrencyToken, ct);
            if (ok) return record;
        }

        throw new InvalidOperationException("Marketplace offer mutation conflicted repeatedly.");
    }

    private async Task<MarketplaceOrder> MutateOrderAsync(
        string realmId,
        string orderId,
        string idempotencyKey,
        Action<MarketplaceOrder> mutate,
        CancellationToken ct)
    {
        const int attempts = 5;

        for (var i = 1; i <= attempts; i++)
        {
            var snap = await _repo.GetOrderSnapshotAsync(realmId, orderId, ct)
                ?? throw new InvalidOperationException("Marketplace order was not found.");

            var record = snap.Record;
            if (record.Metadata.TryGetValue($"op:{idempotencyKey}", out var status) &&
                string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                return record;

            mutate(record);
            record.Metadata[$"op:{idempotencyKey}"] = "completed";
            record.Version++;
            record.UpdatedAtUtc = DateTime.UtcNow;

            var ok = await _repo.TryReplaceOrderAsync(realmId, record, snap.ConcurrencyToken, ct);
            if (ok) return record;
        }

        throw new InvalidOperationException("Marketplace order mutation conflicted repeatedly.");
    }

    private static MarketplaceTransaction BuildTransaction(
        MarketplaceOrder order,
        string buyerCharacterId,
        string sellerCharacterId,
        int quantity,
        MarketplaceTaxBreakdown tax)
    {
        return new MarketplaceTransaction
        {
            TransactionId = $"mkt_tx_{Guid.NewGuid():N}",
            RealmId = order.RealmId,
            OrderId = order.OrderId,
            OrderType = order.OrderType,
            BuyerCharacterId = buyerCharacterId,
            SellerCharacterId = sellerCharacterId,
            ItemDefinitionId = order.ItemDefinitionId,
            Quantity = quantity,
            UnitPriceGold = order.UnitPriceGold,
            GrossGold = tax.GrossGold,
            TaxGold = tax.CompletionTaxGold,
            NetSettlementGold = tax.NetSettlementGold,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private MarketplaceTaxBreakdown ComputeTax(long grossGold)
    {
        var listingFee = ComputePercent(grossGold, _options.ListingFeePercent);
        var completionTax = ComputePercent(grossGold, _options.CompletionTaxPercent);

        return new MarketplaceTaxBreakdown
        {
            GrossGold = grossGold,
            ListingFeeGold = listingFee,
            CompletionTaxGold = completionTax,
            NetSettlementGold = Math.Max(0, grossGold - completionTax)
        };
    }

    private static MarketplaceOfferSnapshot ToOfferSnapshot(MarketplaceOffer offer)
    {
        return new MarketplaceOfferSnapshot
        {
            Offer = offer
        };
    }

    private static long ComputePercent(long amount, decimal percent)
    {
        if (amount <= 0 || percent <= 0m)
            return 0;

        var value = (decimal)amount * (percent / 100m);
        return (long)Math.Ceiling(value);
    }

    private MarketplaceOrderSnapshot ToSnapshot(MarketplaceOrder order, int quantityOverride)
    {
        var gross = checked(order.UnitPriceGold * Math.Max(1, quantityOverride));
        var tax = ComputeTax(gross);
        tax.ListingFeeGold = order.ListingFeePaid;

        return new MarketplaceOrderSnapshot
        {
            Order = order,
            Tax = tax
        };
    }

    private int ResolveDurationHours(int? requested)
    {
        var value = requested ?? _options.DefaultOrderDurationHours;
        if (value <= 0) value = _options.DefaultOrderDurationHours;

        return Math.Clamp(value, 1, Math.Max(1, _options.MaxOrderDurationHours));
    }

    private int ResolveOfferDurationHours(int? requested)
    {
        var value = requested ?? _options.DefaultOfferDurationHours;
        if (value <= 0) value = _options.DefaultOfferDurationHours;

        return Math.Clamp(value, 1, Math.Max(1, _options.MaxOfferDurationHours));
    }

    private static bool IsTerminal(MarketplaceOrderStatus status)
        => status is MarketplaceOrderStatus.Fulfilled or MarketplaceOrderStatus.Cancelled or MarketplaceOrderStatus.Expired;

    private static void ValidateBaseCreate(
        string idempotencyKey,
        string realmId,
        string ownerCharacterId,
        string ownerAccountId,
        string itemDefinitionId,
        int quantity,
        long unitPriceGold)
    {
        ValidateIdempotency(idempotencyKey);
        _ = NormalizeRealm(realmId);

        if (string.IsNullOrWhiteSpace(ownerCharacterId)) throw new InvalidOperationException("OwnerCharacterId is required.");
        if (string.IsNullOrWhiteSpace(ownerAccountId)) throw new InvalidOperationException("OwnerAccountId is required.");
        if (string.IsNullOrWhiteSpace(itemDefinitionId)) throw new InvalidOperationException("ItemDefinitionId is required.");
        if (quantity <= 0) throw new InvalidOperationException("Quantity must be greater than zero.");
        if (unitPriceGold <= 0) throw new InvalidOperationException("UnitPriceGold must be greater than zero.");
    }

    private static void ValidateIdempotency(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new InvalidOperationException("IdempotencyKey is required.");
    }

    private static string NormalizeRealm(string? realmId)
        => string.IsNullOrWhiteSpace(realmId) ? "default" : realmId.Trim();

    private static string MakeStableId(string prefix, string idempotencyKey, string ownerCharacterId, string itemDefinitionId)
    {
        var raw = $"{prefix}:{idempotencyKey}:{ownerCharacterId}:{itemDefinitionId}";
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
