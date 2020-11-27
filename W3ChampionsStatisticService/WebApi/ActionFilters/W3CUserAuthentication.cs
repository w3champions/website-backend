namespace W3ChampionsStatisticService.WebApi.ActionFilters
{
    public class W3CUserAuthenticationDto
    {
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public bool isAdmin { get; set; }
    }
}