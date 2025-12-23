using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Helrift.Gate.Contracts
{
    public sealed class AuthSteamRequest
    {
        public string ticket { get; set; }           // base64 from client
        public string buildVersion { get; set; }     // e.g., "0.6.12+abcd"
        public string steamId { get; set; }          // 64-bit SteamID as string
    }

    // Contracts/AuthSteamResponse.cs
    public sealed class AuthSteamResponse
    {
        public string gateSession { get; set; }      // short-lived JWT
        public string refreshToken { get; set; }     // long-lived opaque
        public object profile { get; set; }          // optional: nickname, avatar, etc.
    }

    public sealed class AuthSteamProfile
    {
        public string steamId { get; set; }
        public string accountId { get; set; }
    }

    public enum AuthFailureReason
    {
        None = 0,

        InvalidRequest = 1,
        SteamVerificationFailed = 2,
        Banned = 3,
        VersionMismatch = 4,
        InvalidSession = 5,
        RealmUnavailable = 6,
        RealmFull = 7
        // Future:
        // Maintenance = 5,
        // InvalidRealm = 6,
    }

    public sealed class AuthFailureResponse
    {
        public int errorCode { get; set; }                      // integer for network efficiency
        public string errorKey { get; set; }                    // optional string enum key ("Banned")
        public long? bannedUntilUnixUtc { get; set; }           // only when banned
        public long? bannedAtUnixUtc { get; set; }              // only when banned
        public string? steamId { get; set; }                    // optional
        public string? reason { get; set; }
        public int? retryAfterSeconds { get; set; }            // client countdown
        public long? shutdownAtUnixUtc { get; set; }           // absolute time (optional but helpful)
        public string? realmMessage { get; set; }              // friendly text
    }

    public sealed class AuthRefreshRequest
    {
        public string refreshToken { get; set; }
        public string buildVersion { get; set; }  // optional but handy to re-check version
    }
}