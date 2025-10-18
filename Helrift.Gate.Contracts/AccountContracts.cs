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
        public PasswordData Password { get; set; }
        public string EmailAddress { get; set; }
        public DateTime LastLogIn { get; set; }

        public CharacterData[] Characters { get; set; }
        public Dictionary<string, EntitlementData> Entitlements { get; set; }
        public List<string> OwnedUnlockableIds { get; set; }
    }

    [Serializable]
    public class EntitlementData
    {
        public string Id;
        public string Since;
        public int PurchaseCount;
    }

    [Serializable]
    public class PasswordData
    {
        public string Salt { get; set; }
        public string Hash { get; set; }
    }
}
