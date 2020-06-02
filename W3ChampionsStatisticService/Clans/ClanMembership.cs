using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Clans
{
    public class ClanMembership : IIdentifiable
    {
        public string BattleTag { get; set; }
        [JsonIgnore]
        public ObjectId? ClanId { get; set; }
        [JsonIgnore]
        public ObjectId? PendingInviteFromClan { get; set; }

        [JsonPropertyName("clanId")]
        public string ClanIdRaw => ClanId?.ToString();

        [JsonPropertyName("pendingInviteFromClan")]
        public string PendingInviteFromClanRaw => PendingInviteFromClan?.ToString();
        [JsonIgnore]
        public string Id => BattleTag;
        public string ClanName { get; set; }

        public void JoinClan(Clan clan)
        {
            if (ClanId != null) throw new ValidationException("User Allready in clan");
            if (clan.Id != PendingInviteFromClan) throw new ValidationException("Invite to another clan still pending");

            ClanId = clan.Id;
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
            PendingInviteFromClan = clan.Id;
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