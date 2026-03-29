using System;
using System.Collections.Generic;

namespace Helrift.Gate.Contracts
{
    [Serializable]
    public struct Vector3DTO
    {
        public float x;
        public float y;
        public float z;

        public Vector3DTO(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public enum RealmEventState { Inactive, Running, Ended, Aborted }
    public enum CrusadePhase { Setup, Active, Cleansing, Ended }
    public enum TownSide { Neutral, Aresden, Elvine, Middleland }
    public enum StoneControlState { Neutral, ControlledAresden, ControlledElvine, Contested }
    public enum CrusadeBuildingType { Shop, Blacksmith, Cityhall, Warehouse }

    [Serializable]
    public class CrusadeStoneState
    {
        public string StoneId;
        public Vector3DTO Position;
        public StoneControlState ControlState;
        public float ControlPercent;
    }

    [Serializable]
    public class CrusadeBuildingState
    {
        public string BuildingId;
        public CrusadeBuildingType BuildingType;
        public Vector3DTO ShieldVfxPosition;
        public int ShieldValue;
        public int Damage;
        public bool IsDestroyed;
    }

    [Serializable]
    public class CrusadeSnapshotPayload
    {
        public string InstanceId;
        public CrusadePhase Phase;
        public float ManaPercentAresden;
        public float ManaPercentElvine;
        public List<CrusadeStoneState> Stones;
        public List<CrusadeBuildingState> Buildings;
        public long Sequence;
        public DateTime Utc;
    }

    [Serializable]
    public class CrusadeFeedBase
    {
        public string InstanceId;
        public long Sequence;
        public DateTime Utc;
    }

    [Serializable]
    public class CrusadeFeed_MeteorVolleyStarted : CrusadeFeedBase
    {
        public int MeteorCount;
        public float TelegraphSeconds;
        public Vector3DTO TargetAreaCenter;
        public float TargetRadius;
        public float Duration;
        public TownSide TargetSide;
    }

    [Serializable]
    public class CrusadeFeed_MeteorImpact : CrusadeFeedBase
    {
        public string MeteorId;
        public Vector3DTO ImpactPosition;
        public CrusadeBuildingType? HitBuildingType;
        public string HitBuildingId;
        public int ShieldRemaining;
        public int BuildingDamage;
    }

    [Serializable]
    public class CrusadeFeed_BuildingShieldChanged : CrusadeFeedBase
    {
        public string BuildingId;
        public int ShieldValue;
    }

    [Serializable]
    public class CrusadeFeed_BuildingDamaged : CrusadeFeedBase
    {
        public string BuildingId;
        public int DamageApplied;
        public int TotalDamage;
    }

    [Serializable]
    public class CrusadeFeed_BuildingDestroyed : CrusadeFeedBase
    {
        public string BuildingId;
        public CrusadeBuildingType BuildingType;
    }

    [Serializable]
    public class CrusadeFeed_StoneControlChanged : CrusadeFeedBase
    {
        public string StoneId;
        public StoneControlState ControlState;
        public float ControlPercent;
    }

    [Serializable]
    public class CrusadeFeed_StartedEndedAborted : CrusadeFeedBase
    {
        public string Result;
        public string WinnerSide;
        public CrusadeSummarySummary Summary;
    }

    [Serializable]
    public class CrusadeParticipant
    {
        public string CharacterId;
        public float TimeInZoneSeconds;
        public int ContributionPoints;
    }

    [Serializable]
    public class CrusadeSummarySummary
    {
        public string InstanceId { get; set; }
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc { get; set; }
        public List<CrusadeParticipant> Participants { get; set; }
        public Dictionary<string, int> AggregateContributions { get; set; }
        public string Result { get; set; }
    }

    // RealmEventEnvelope (exact contract)
    [Serializable]
    public class RealmEventEnvelope
    {
        public string Type { get; set; }              // e.g., "crusade.snapshot" or "crusade.feed"
        public string RealmId { get; set; }            // e.g., "middleland"
        public string EventType { get; set; }         // e.g., "Crusade"
        public string EventInstanceId { get; set; }    // GUID for this event instance
        public long Sequence { get; set; }            // monotonic sequence number from host
        public DateTime Utc { get; set; }            // timestamp (UTC)
        public string PayloadJson { get; set; }       // JSON-serialized payload

        public RealmEventEnvelope() { }

        public RealmEventEnvelope(string type, string realmId, string eventType, string eventInstanceId, long seq, DateTime utc, string payloadJson)
        {
            Type = type;
            RealmId = realmId;
            EventType = eventType;
            EventInstanceId = eventInstanceId;
            Sequence = seq;
            Utc = utc;
            PayloadJson = payloadJson;
        }
    }
}