namespace W3ChampionsStatisticService.WebApi.ActionFilters
{
    public class W3CUserAuthenticationDto
    {
        public string BattleTag { get; set; }
        public string Name => BattleTag.Split("#")[0];
        public bool isAdmin { get; set; }
    }
}