namespace W3ChampionsStatisticService.PlayerProfiles.War3InfoPlayerAkas;

public class PlayerAka
{
    public int id { get; set; } // id Warcraft3.info uses for their URL link, 
    // e.g. warcraft3.info/stats/player/204 => Lyn's Warcraft3.info profile
    public int stats_player_id { get; set; }
    public string aka { get; set; }
    public string platform { get; set; }
    public int changed_by { get; set; }
    public string created_at { get; set; }
    public string updated_at { get; set; }
    public Player player { get; set; } // player object
}
