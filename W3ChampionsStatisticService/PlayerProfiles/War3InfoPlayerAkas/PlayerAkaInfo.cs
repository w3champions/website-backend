namespace W3ChampionsStatisticService.PlayerProfiles.War3InfoPlayerAkas
{
    public class PlayerAkaInfo
    {
        // same as StatsPlayerId, used for warcraft3.info URL
        public int Id { get; set; }
        // The traditional anglicised name of the player as recognised by the community
        public string Name { get; set; }
        public string Mainrace { get; set; }
        // 2-letter country code
        public string Country { get; set; }
        // name used in the liquipedia URL, e.g. "Lyn" for liquipedia.net/warcraft/Lyn
        public string Liquipedia { get; set; }
    }
}