using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;
using System.Security.Cryptography;
using System.Text;

namespace Helrift.Gate.Api.Services.Escrow;

public sealed class CharacterEscrowBalanceService : IEscrowBalanceService
{
    private readonly IGameDataProvider _game;
    private readonly ILogger<CharacterEscrowBalanceService> _logger;

    public CharacterEscrowBalanceService(IGameDataProvider game, ILogger<CharacterEscrowBalanceService> logger)
    {
        _game = game;
        _logger = logger;
    }

    public IReadOnlyList<EscrowCapability> GetCapabilities()
        => new[]
        {
            new EscrowCapability { AssetType = EscrowAssetType.ItemInstance, Key = "item_instance", Status = EscrowSupportStatus.Supported, Note = "Full CharacterItemData payload transfer." },
            new EscrowCapability { AssetType = EscrowAssetType.PointBalance, Key = "ek", Status = EscrowSupportStatus.Supported, Note = "Character.EnemyKillPoints." },
            new EscrowCapability { AssetType = EscrowAssetType.PointBalance, Key = "majesties", Status = EscrowSupportStatus.Supported, Note = "Character.MajesticPoints / Progression.majesticPoints." },
            new EscrowCapability { AssetType = EscrowAssetType.PointBalance, Key = "divine_favour", Status = EscrowSupportStatus.Supported, Note = "Character.Progression.divineFavour." },
            new EscrowCapability { AssetType = EscrowAssetType.PointBalance, Key = "contribution", Status = EscrowSupportStatus.Partial, Note = "Mapped to stats:contribution until canonical ledger exists." },
            new EscrowCapability { AssetType = EscrowAssetType.Currency, Key = "gold", Status = EscrowSupportStatus.NotSupported, Note = "No canonical character gold ledger in current Gate model." }
        };

    public bool IsSupported(string balanceKey)
    {
        var k = Normalize(balanceKey);
        if (k == "gold") return false;
        if (k == "ek" || k == "enemykillpoints" || k == "enemy_kill_points") return true;
        if (k == "majesties" || k == "majesticpoints" || k == "majestic_points") return true;
        if (k == "divinefavour" || k == "divine_favour") return true;
        if (k == "contribution" || k == "stats:contribution") return true;
        return k.StartsWith("stats:", StringComparison.Ordinal);
    }

    public async Task EnsureDebitAsync(EscrowBalanceMutationRequest request, CancellationToken ct)
    {
        await EnsureMutationAsync(request, isDebit: true, ct);
    }

    public async Task EnsureCreditAsync(EscrowBalanceMutationRequest request, CancellationToken ct)
    {
        await EnsureMutationAsync(request, isDebit: false, ct);
    }

    private async Task EnsureMutationAsync(EscrowBalanceMutationRequest request, bool isDebit, CancellationToken ct)
    {
        if (!IsSupported(request.BalanceKey))
            throw new InvalidOperationException($"Balance '{request.BalanceKey}' is not supported for escrow.");

        if (request.Amount <= 0)
            throw new InvalidOperationException("Escrow balance amount must be > 0.");

        var character = await _game.GetCharacterAsync(request.AccountId, request.CharacterId, ct)
            ?? throw new InvalidOperationException("Target character not found for balance mutation.");

        character.Stats ??= new Dictionary<string, long>(StringComparer.Ordinal);
        var marker = BuildMarker(request.MutationKey, request.BalanceKey, isDebit);

        if (character.Stats.ContainsKey(marker))
            return;

        var normalized = Normalize(request.BalanceKey);
        var current = ReadBalance(character, normalized);

        if (isDebit && current < request.Amount)
            throw new InvalidOperationException($"Insufficient balance for '{request.BalanceKey}'.");

        var next = isDebit ? current - request.Amount : current + request.Amount;
        WriteBalance(character, normalized, next);

        // Local idempotency marker persisted with same character document update.
        character.Stats[marker] = request.Amount;

        await _game.SaveCharacterAsync(character, ct);

        _logger.LogInformation("Escrow balance mutation applied: key={Key} debit={Debit} amount={Amount} char={CharacterId} marker={Marker}",
            request.BalanceKey, isDebit, request.Amount, request.CharacterId, marker);
    }

    private static string BuildMarker(string mutationKey, string balanceKey, bool isDebit)
    {
        var raw = $"{mutationKey}|{Normalize(balanceKey)}|{(isDebit ? "debit" : "credit")}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return $"escrow_op:{hash[..24]}";
    }

    private static string Normalize(string key)
    {
        var k = (key ?? string.Empty).Trim().ToLowerInvariant();
        return k switch
        {
            "enemykillpoints" or "enemy_kill_points" => "ek",
            "majesticpoints" or "majestic_points" => "majesties",
            "divinefavour" => "divine_favour",
            "contribution" => "stats:contribution",
            _ => k
        };
    }

    private static long ReadBalance(CharacterData c, string key)
    {
        return key switch
        {
            "ek" => c.EnemyKillPoints,
            "majesties" => c.MajesticPoints,
            "divine_favour" => c.Progression?.divineFavour ?? 0,
            _ when key.StartsWith("stats:", StringComparison.Ordinal) =>
                c.Stats != null && c.Stats.TryGetValue(key["stats:".Length..], out var v) ? v : 0,
            _ => throw new InvalidOperationException($"Unsupported balance key '{key}'.")
        };
    }

    private static void WriteBalance(CharacterData c, string key, long value)
    {
        if (value < 0) throw new InvalidOperationException("Balance cannot be negative.");

        switch (key)
        {
            case "ek":
                c.EnemyKillPoints = checked((int)value);
                break;
            case "majesties":
                c.MajesticPoints = checked((int)value);
                c.Progression ??= new CharacterProgressionData();
                c.Progression.majesticPoints = checked((int)value);
                break;
            case "divine_favour":
                c.Progression ??= new CharacterProgressionData();
                c.Progression.divineFavour = checked((int)value);
                break;
            default:
                if (!key.StartsWith("stats:", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Unsupported balance key '{key}'.");
                c.Stats ??= new Dictionary<string, long>(StringComparer.Ordinal);
                c.Stats[key["stats:".Length..]] = value;
                break;
        }
    }
}