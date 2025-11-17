using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Helrift.Gate.App.Domain
{
    public static class PartyMapper
    {
        public static PartyDto ToDto(Party party) =>
        new PartyDto
        {
            PartyId = party.Id,
            LeaderCharacterId = party.LeaderCharacterId,
            PartyName = party.PartyName,
            Side = party.Side.ToString(),
            ExpMode = party.ExpMode,
            Visibility = party.Visibility.ToString(),
            Members = party.Members.Select(m => new PartyMemberDto
            {
                CharacterId = m.CharacterId,
                CharacterName = m.CharacterName
            }).ToList()
        };
    }
}
