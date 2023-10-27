using System.Collections.Generic;

namespace W3ChampionsStatisticService.Clans.ClanStates;

public abstract class ClanState
{
    public List<string> FoundingFathers { get; set; } = new List<string>();
    public List<string> Members { get; set; } = new List<string>();
    public List<string> Shamans { get; set; } = new List<string>();
    public string ChiefTain { get; set; }

    public abstract ClanState AcceptInvite(ClanMembership membership);

    public abstract ClanState LeaveClan(ClanMembership clanMemberShip);
}
