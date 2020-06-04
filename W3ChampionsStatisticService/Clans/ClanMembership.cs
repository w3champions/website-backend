using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Clans
{
    public class ClanMembership : IIdentifiable
    {
        public string BattleTag { get; set; }
        [JsonIgnore]
        public string ClanId { get; set; }
        [JsonIgnore]
        public string PendingInviteFromClan { get; set; }

        [JsonPropertyName("pendingInviteFromClan")]
        public string PendingInviteFromClanRaw => PendingInviteFromClan;
        [JsonIgnore]
        public string Id => BattleTag;
        public string ClanName { get; set; }

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
}