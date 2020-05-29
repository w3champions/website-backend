using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.Clans
{
    public class Clan
    {
        [JsonIgnore]
        public ObjectId Id { get; set; }

        [JsonPropertyName("id")]
        public string IdRaw => Id.ToString();
        public string ClanName { get; set; }
        public string ChiefTain { get; set; }

        public bool IsSuccesfullyFounded { get; set; }

        public List<string> Members { get; set; } = new List<string>();
        public List<string> FoundingFathers { get; set; } = new List<string>();
        public List<string> Shamans { get; set; } = new List<string>();
        public List<string> PendingInvites { get; set; } = new List<string>();

        public static Clan Create(string clanName, ClanMembership founder)
        {
            var trim = clanName.Trim();
            if (trim.Length < 3)
            {
                throw new ValidationException("Name too short");
            }

            var clan = new Clan
            {
                ClanName = clanName,
                ChiefTain = founder.BattleTag,
                FoundingFathers = new List<string> { founder.BattleTag }
            };

            return clan;
        }

        public void Sign(ClanMembership membership)
        {
            if (IsSuccesfullyFounded)
            {
                throw new ValidationException("Can not sign final clan anymore");
            }

            if (FoundingFathers.Contains(membership.BattleTag))
            {
                throw new ValidationException("Can not sign Clan Founding twice");
            }

            membership.SignForClan(this);

            FoundingFathers.Add(membership.BattleTag);

            if (FoundingFathers.Count >= 7)
            {
                IsSuccesfullyFounded = true;
                Members = FoundingFathers;
            }
        }

        public void AddMember(ClanMembership membership)
        {
            if (!IsSuccesfullyFounded)
            {
                throw new ValidationException("Clan not founded yet");
            }

            if (Members.Contains(membership.BattleTag))
            {
                throw new ValidationException("Can not participate in clan twice");
            }

            membership.JoinClan(this);
            Members.Add(membership.BattleTag);
        }

        public void Invite(ClanMembership clanMemberShip, string personWhoInvitesBattleTag)
        {
            if (!IsSuccesfullyFounded)
            {
                throw new ValidationException("Clan not founded yet");
            }

            if (ChiefTain != personWhoInvitesBattleTag && !Shamans.Contains(personWhoInvitesBattleTag))
            {
                throw new ValidationException("Only chieftains and shamans can invite");
            }

            clanMemberShip.Invite(this);

            if (PendingInvites.Contains(clanMemberShip.BattleTag))
            {
                throw new ValidationException("Can not invite player twice");
            }

            PendingInvites.Add(clanMemberShip.BattleTag);
        }
    }
}