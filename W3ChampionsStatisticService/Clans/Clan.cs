using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using W3ChampionsStatisticService.Clans.ClanStates;

namespace W3ChampionsStatisticService.Clans
{
    public class Clan
    {
        [JsonIgnore]
        public ObjectId Id { get; set; }
        [JsonIgnore]
        public ClanState ClanState { get; set; }

        [JsonPropertyName("id")]
        public string IdRaw => Id.ToString();
        public string ClanName { get; set; }
        public string ChiefTain { get; set; }

        public bool IsSuccesfullyFounded => ClanState.GetType() == typeof(FoundedClan);

        public List<string> Members => ClanState.Members;
        public List<string> FoundingFathers => ClanState.FoundingFathers;
        public List<string> Shamans => ClanState.Shamans;
        public List<string> PendingInvites { get; set; } = new List<string>();

        public static Clan Create(string clanName, ClanMembership founder)
        {
            var trim = clanName.Trim();
            if (!(founder.ClanId == null || founder.ClanId == ObjectId.Empty)) throw new ValidationException("Founder can not be in another clan");
            if (trim.Length < 3) throw new ValidationException("Name too short");

            var clan = new Clan
            {
                ClanName = trim,
                ChiefTain = founder.BattleTag,
                ClanState = new NotFoundedClan(founder.BattleTag)
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
            if (FoundingFathers.Contains(clanMemberShip.BattleTag)) throw new ValidationException("Can not invite player twice");

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
            if (clanMemberShip.BattleTag == ChiefTain) throw new ValidationException("Chieftain can not leave cal, transfer ownership first");
            clanMemberShip.LeaveClan();

            ClanState = ClanState.LeaveClan(clanMemberShip);

            if (!IsSuccesfullyFounded)
            {
                FoundingFathers.Remove(clanMemberShip.BattleTag);
            }
            else
            {
                Members.Remove(clanMemberShip.BattleTag);
            }
        }

        public void AddShaman(string shamanId, string actingPlayer)
        {
            if (ChiefTain != actingPlayer) throw new ValidationException("Only Chieftain can manage Shamans");
            if (!Members.Contains(shamanId)) throw new ValidationException("Shaman has to be in clan");
            if (shamanId == ChiefTain) throw new ValidationException("Chieftain can not be made Shaman");
            if (Shamans.Contains(shamanId)) throw new ValidationException("Player is already Shaman");

            Shamans.Add(shamanId);
        }

        public void RemoveShaman(string shamanId, string actingPlayer)
        {
            if (ChiefTain != actingPlayer) throw new ValidationException("Only Chieftain can manage Shamans");

            Shamans.Remove(shamanId);
        }

        public void KickPlayer(ClanMembership clanMemberShip, string actingPlayer)
        {
            if (ChiefTain != actingPlayer && !Shamans.Contains(actingPlayer)) throw new ValidationException("Only Chieftain or shamans can kick players");
            if (!Members.Contains(clanMemberShip.BattleTag)) throw new ValidationException("Player not in this clan");
            if (clanMemberShip.BattleTag == ChiefTain) throw new ValidationException("Can not kick chieftain");

            clanMemberShip.LeaveClan();
            Members.Remove(clanMemberShip.BattleTag);
            Shamans.Remove(clanMemberShip.BattleTag);
        }
    }
}