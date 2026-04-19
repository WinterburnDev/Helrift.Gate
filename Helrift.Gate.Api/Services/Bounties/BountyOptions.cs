namespace Helrift.Gate.Api.Services.Bounties;

public sealed class BountyOptions
{
    public long MinBountyGold { get; set; } = 100;
    public long MaxBountyGold { get; set; } = 1_000_000;
    public decimal ListingFeePercent { get; set; } = 2m;
    public decimal CancellationFeePercent { get; set; } = 0m;
    public int DefaultBountyDurationHours { get; set; } = 24;
    public int MaxBountyDurationHours { get; set; } = 168;
    public int MaxActiveBountiesPerIssuer { get; set; } = 10;
    public bool AllowMultipleActivePerTarget { get; set; } = true;
    public bool DisallowSameAccountKillPayout { get; set; } = true;
    public int ResolveKillPairCooldownSeconds { get; set; } = 0;
    public string GoldItemDefinitionId { get; set; } = "gold";
}
