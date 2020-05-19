namespace W3ChampionsStatisticService.Authorization
{
    public class BlizzardUserInfo
    {
        public string sub { get; set; }
        public string id { get; set; }
        public string battletag { get; set; }
        public string name => battletag.Split("#")[0];
    }
}