namespace W3ChampionsStatisticService.PlayerProfiles.War3InfoPlayerAkas
{
    public class PlayerAka
    {
        // this is the id Warcraft3.info uses for their URL link, e.g. warcraft3.info/stats/player/204 => Lyn's Warcraft3.info profile
        public int StatsPlayerId { get; set; }
        // Player's Bnet aka
        public string Aka { get; set; }
        // player object
        public PlayerAkaInfo player { get; set; }
    }
}
