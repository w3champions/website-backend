using System;
using System.Linq;
using W3ChampionsStatisticService.Admin;

namespace W3ChampionsStatisticService.WebApi.ActionFilters
{
    public class W3CUserAuthenticationDto
    {
        public string BattleTag { get; set; }
        public string Name => BattleTag.Split("#")[0];
        public Boolean isAdmin { get { return Admins.ApprovedAdmins.Any(p => p == BattleTag.ToLower()); } }
    }
}