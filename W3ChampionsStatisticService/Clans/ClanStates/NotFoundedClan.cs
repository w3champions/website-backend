using System.Collections.Generic;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Clans.ClanStates;

[Trace]
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

        if (FoundingFathers.Count >= 2)
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
