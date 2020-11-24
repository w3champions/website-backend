using System;
using System.Linq;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Authorization
{
    public class W3CUserAuthentication : IIdentifiable
    {
        public static W3CUserAuthentication Create(string battletag)
        {
            return new W3CUserAuthentication
            {
                Token = Guid.NewGuid().ToString(),
                BattleTag = battletag
            };
        }

        public string Token { get; set; }
        public string BattleTag { get; set; }
        public string Name => BattleTag.Split("#")[0];
        public Boolean isAdmin { get { return Admins.ApprovedAdmins.Any(p => p == BattleTag.ToLower()); } }
        public string Id => BattleTag;
    }
}