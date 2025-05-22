using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.Clans.ClanStates;
using W3ChampionsStatisticService.Ladder;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Clans;

[Trace]
public class Clan
{
    [JsonIgnore]
    public ClanState ClanState { get; set; }

    public string ClanName { get; set; }

    [BsonId]
    public string ClanId { get; set; }
    public string ChiefTain => ClanState.ChiefTain;

    public bool IsSuccesfullyFounded => ClanState.GetType() == typeof(FoundedClan);

    public List<string> Members => ClanState.Members;
    public List<string> FoundingFathers => ClanState.FoundingFathers;
    public List<string> Shamans => ClanState.Shamans;
    public List<string> PendingInvites { get; set; } = new List<string>();
    public List<Rank> Ranks { get; set; } = new List<Rank>();

    public static Clan Create(string clanName, string clanAbbrevation, ClanMembership founder)
    {
        if (!(founder.ClanId == null || string.IsNullOrWhiteSpace(founder.ClanId))) throw new ValidationException("Founder can not be in another clan");

        var clan = new Clan
        {
            ClanName = clanName,
            ClanState = new NotFoundedClan(founder.BattleTag),
            ClanId = clanAbbrevation,
        };

        return clan;
    }

    public void AcceptInvite(ClanMembership membership)
    {
        if (Members.Contains(membership.BattleTag)) throw new ValidationException("Can not participate in clan twice");
        if (!PendingInvites.Contains(membership.BattleTag)) throw new ValidationException("Player was not invites to sign the clan");

        membership.JoinClan(this);

        ClanState = ClanState.AcceptInvite(membership);

        PendingInvites.Remove(membership.BattleTag);
    }

    public void Invite(ClanMembership clanMemberShip, string personWhoInvitesBattleTag)
    {
        if (ChiefTain != personWhoInvitesBattleTag && !Shamans.Contains(personWhoInvitesBattleTag)) throw new ValidationException("Only chieftains and shamans can invite");
        if (PendingInvites.Contains(clanMemberShip.BattleTag)) throw new ValidationException("Can not invite player twice");
        if (Members.Contains(clanMemberShip.BattleTag)) throw new ValidationException("Can not invite player twice");

        clanMemberShip.Invite(this);

        PendingInvites.Add(clanMemberShip.BattleTag);
    }

    public void RevokeInvite(ClanMembership clanMemberShip, string personWhoInvitesBattleTag)
    {
        if (ChiefTain != personWhoInvitesBattleTag && !Shamans.Contains(personWhoInvitesBattleTag)) throw new ValidationException("Only chieftains and shamans can invite");

        clanMemberShip.RevokeInvite();

        PendingInvites.Remove(clanMemberShip.BattleTag);
    }

    public void RejectInvite(ClanMembership clanMemberShip)
    {
        clanMemberShip.RevokeInvite();

        PendingInvites.Remove(clanMemberShip.BattleTag);
    }

    public void LeaveClan(ClanMembership clanMemberShip)
    {
        if (clanMemberShip.BattleTag == ChiefTain) throw new ValidationException("Chieftain can not leave clan, transfer ownership first");
        clanMemberShip.LeaveClan();

        ClanState = ClanState.LeaveClan(clanMemberShip);

        if (!IsSuccesfullyFounded)
        {
            FoundingFathers.Remove(clanMemberShip.BattleTag);
        }
        else
        {
            Members.Remove(clanMemberShip.BattleTag);
            Shamans.Remove(clanMemberShip.BattleTag);
        }
    }

    public void AddShaman(string shamanId, string actingPlayer)
    {
        if (ChiefTain != actingPlayer) throw new ValidationException("Only Chieftain can manage Shamans");
        if (!Members.Contains(shamanId)) throw new ValidationException("Shaman has to be in clan");
        if (shamanId == ChiefTain) throw new ValidationException("Chieftain can not be made Shaman");
        if (Shamans.Contains(shamanId)) throw new ValidationException("Player is already Shaman");

        Members.Remove(shamanId);
        Shamans.Add(shamanId);
    }

    public void RemoveShaman(string shamanId, string actingPlayer)
    {
        if (ChiefTain != actingPlayer) throw new ValidationException("Only Chieftain can manage Shamans");

        Shamans.Remove(shamanId);
        Members.Add(shamanId);
    }

    public void KickPlayer(ClanMembership clanMemberShip, string actingPlayer)
    {
        if (ChiefTain != actingPlayer && !Shamans.Contains(actingPlayer)) throw new ValidationException("Only Chieftain or shamans can kick players");
        if (!Members.Contains(clanMemberShip.BattleTag) && !Shamans.Contains(clanMemberShip.BattleTag))
            throw new ValidationException("Player not in this clan");
        if (clanMemberShip.BattleTag == ChiefTain) throw new ValidationException("Can not kick chieftain");

        clanMemberShip.LeaveClan();
        Members.Remove(clanMemberShip.BattleTag);
        Shamans.Remove(clanMemberShip.BattleTag);
    }

    public void SwitchChieftain(string newChieftain, string actingPlayer)
    {
        if (ChiefTain != actingPlayer) throw new ValidationException("Only Chieftain can switch to new Chieftain");
        if (!Shamans.Contains(newChieftain)) throw new ValidationException("Only Shaman can be promoted to Chieftain");

        Shamans.Remove(newChieftain);
        Shamans.Add(ChiefTain);
        ClanState.ChiefTain = newChieftain;
    }
}
