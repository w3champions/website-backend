using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.Clans
{
    public class Clan
    {
        public ObjectId Id { get; set; }
        public string ClanName { get; set; }

        public static Clan Create(string clanName)
        {
            var trim = clanName.Trim();
            if (trim.Length < 3)
            {
                throw new ValidationException("Name too short");
            }

            return new Clan
            {
                ClanName = clanName
            };
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

            membership.ParticipateInClan(this);

            FoundingFathers.Add(membership.BattleTag);

            if (FoundingFathers.Count >= 7)
            {
                IsSuccesfullyFounded = true;
                Members = FoundingFathers;
            }
        }

        public bool IsSuccesfullyFounded { get; set; }

        public List<string> Members { get; set; } = new List<string>();
        public List<string> FoundingFathers { get; set; } = new List<string>();
        public List<string> PendingInvites { get; set; } = new List<string>();

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

            membership.ParticipateInClan(this);
            Members.Add(membership.BattleTag);
        }

        public void Invite(ClanMembership clanMemberShip)
        {
            clanMemberShip.Invite(this);

            if (PendingInvites.Contains(clanMemberShip.BattleTag))
            {
                throw new ValidationException("Can not invite player twice");
            }

            PendingInvites.Add(clanMemberShip.BattleTag);
        }
    }
}