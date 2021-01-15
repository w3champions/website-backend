namespace W3ChampionsStatisticService.PlayerProfiles.War3InfoPlayerAkas
{
    // Not currently implemented for any endpoint - for future potential use
    public class Player
    {
        public int id { get; set; } // same as StatsPlayerId, used for warcraft3.info URL       
        public string name { get; set; } // The anglicised nickname of the player by the community (their aka)
        public string main_race { get; set; }       
        public string country { get; set; } // 2-letter country code
        public string liquipedia { get; set; } // name used in the liquipedia URL, e.g. "Lyn" for liquipedia.net/warcraft/Lyn
    }
}