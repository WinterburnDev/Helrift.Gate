// Helrift.Gate.Contracts/AccountContracts.cs
using System;
using System.Collections.Generic;

namespace Helrift.Gate.Contracts
{
    [Serializable]
    public class AccountData
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string EmailAddress { get; set; }
        public DateTime LastLogIn { get; set; }
        public CharacterData[] Characters { get; set; }
        public List<string> OwnedUnlockableIds { get; set; }
    }

    public sealed class NewAccountRequest
    {
        public required string SteamId64 { get; init; }
        public string? Username { get; init; }
        public string? EmailAddress { get; init; }
    }
}
