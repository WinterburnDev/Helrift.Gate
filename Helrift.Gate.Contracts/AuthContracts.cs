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
}