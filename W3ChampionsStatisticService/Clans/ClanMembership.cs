using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Clans;

public class ClanMembership : IIdentifiable, IVersionable
{
    public string BattleTag { get; set; }
    public string ClanId { get; set; }
    public string PendingInviteFromClan { get; set; }
    public string ClanName { get; set; }
    public DateTimeOffset LastUpdated  { get; set; }

    [JsonIgnore]
    public string Id => BattleTag;
    public void JoinClan(Clan clan)
    {
        if (ClanId != null) throw new ValidationException("User Allready in clan");
        if (clan.ClanId != PendingInviteFromClan) throw new ValidationException("Invite to another clan still pending");

        ClanId = clan.ClanId;
        PendingInviteFromClan = null;
        ClanName = clan.ClanName;
    }

    public static ClanMembership Create(string battleTag)
    {
        return new ClanMembership
        {
            BattleTag = battleTag
        };
    }

    public void Invite(Clan clan)
    {
        if (PendingInviteFromClan != null) throw new ValidationException("Player already invited to different clan");
        if (ClanId != null) throw new ValidationException("Player already part of a different clan");

        PendingInviteFromClan = clan.ClanId;
        ClanName = clan.ClanName;
    }

    public void LeaveClan()
    {
        ClanId = null;
        ClanName = null;
    }

    public void RevokeInvite()
    {
        PendingInviteFromClan = null;
        ClanName = null;
    }
}
