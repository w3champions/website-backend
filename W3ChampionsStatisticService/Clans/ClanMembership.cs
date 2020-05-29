using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Clans
{
    public class ClanMembership : IIdentifiable
    {
        public string BattleTag { get; set; }
        public ObjectId? ClanId { get; set; }
        public string Id => BattleTag;

        public void JoinClan(Clan clan)
        {
            if (ClanId != null) throw new ValidationException("User Allready in clan");
            if (clan.Id != PendingInviteFromClan) throw new ValidationException("Invite to another clan still pending");

            ClanId = clan.Id;
        }

        public void SignForClan(Clan clan)
        {
            if (ClanId != null) throw new ValidationException("User Allready in clan");

            ClanId = clan.Id;
        }

        public ObjectId? PendingInviteFromClan { get; set; }

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
        }

        public void ExitClan()
        {
            ClanId = null;
        }
    }
}