using System.Collections.Generic;

namespace W3ChampionsStatisticService.Clans.ClanStates;

public class NotFoundedClan : ClanState
{
    public NotFoundedClan(string founder)
    {
        FoundingFathers = new List<string> { founder };
        ChiefTain = founder;
    }

    public override ClanState AcceptInvite(ClanMembership membership)
    {
        FoundingFathers.Add(membership.BattleTag);

        if (FoundingFathers.Count >= 7)
        {
            return new FoundedClan(FoundingFathers, ChiefTain);
        }

        return this;
    }

    public override ClanState LeaveClan(ClanMembership clanMemberShip)
    {
        FoundingFathers.Remove(clanMemberShip.BattleTag);
        return this;
    }
}
