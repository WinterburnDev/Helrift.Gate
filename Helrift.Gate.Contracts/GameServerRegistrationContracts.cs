using System.Collections.Generic;

namespace Helrift.Gate.Contracts
{
    public sealed class GameServerRegistrationDto
    {
        public string gameServerId { get; set; }
        public string buildVersion { get; set; }
        public long registeredAtUnixUtc { get; set; }
        public RealmConfigDto realmConfig { get; set; }
        public List<MapInfoDto> maps { get; set; } = new();
    }

    public sealed class RealmConfigDto
    {
        public int travellerMaxLevel { get; set; }
        public double expMultiplier { get; set; }
        public double insightsMultiplier { get; set; }
        public double ekMultiplier { get; set; }
        public bool travellersCanPvp { get; set; }
        public bool restrictPvpToZones { get; set; }
        public bool citizensCanEnterNeutralBuildings { get; set; }
        public bool citizensRecallToNeutralMap { get; set; }
        public bool allowMaxLevelCitizenship { get; set; }
        public bool freeRepairsEnabled { get; set; }
        public bool playersCanChangeCitizenship { get; set; }
        public int maximumLevel { get; set; }
        public int maximumCharacters { get; set; }
        public int maximumPartyMembers { get; set; }
    }

    public sealed class MapInfoDto
    {
        public string id { get; set; }
        public string mapName { get; set; }
        public string sceneName { get; set; }
        public int cellX { get; set; }
        public int cellY { get; set; }
        public bool isSafeMap { get; set; }
        public bool isOutside { get; set; }
    }
}