using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace W3ChampionsStatisticService.Clans.ClanStates
{
    public class FoundedClan : ClanState
    {
        public FoundedClan(List<string> foundingFathers)
        {
            FoundingFathers = foundingFathers;
            Members = foundingFathers;
        }

        public override ClanState AcceptInvite(ClanMembership membership)
        {
            Members.Add(membership.BattleTag);
            return this;
        }

        public override ClanState LeaveClan(ClanMembership clanMemberShip)
        {
            Members.Remove(clanMemberShip.BattleTag);
            return this;
        }
    }
}