using System.Collections.Generic;

namespace W3ChampionsStatisticService.Clans.ClanStates
{
    public class NotFoundedClan : ClanState
    {
        public NotFoundedClan(string founder)
        {
            FoundingFathers = new List<string> { founder };
            Members = FoundingFathers;
            ChiefTain = founder;
        }

        public override ClanState AcceptInvite(ClanMembership membership)
        {
            Members.Add(membership.BattleTag);
            FoundingFathers.Add(membership.BattleTag);

            if (FoundingFathers.Count >= 7)
            {
                return new FoundedClan(FoundingFathers, ChiefTain);
            }

            return this;
        }

        public override ClanState LeaveClan(ClanMembership clanMemberShip)
        {
            Members.Remove(clanMemberShip.BattleTag);
            FoundingFathers.Remove(clanMemberShip.BattleTag);
            return this;
        }
    }
}