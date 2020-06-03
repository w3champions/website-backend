using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.Clans.ClanStates
{
    public class FoundedClan : ClanState
    {
        public FoundedClan(List<string> foundingFathers, string chiefTain)
        {
            FoundingFathers = foundingFathers;
            Members = foundingFathers.Where(f => f != chiefTain).ToList();
            ChiefTain = chiefTain;
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