namespace W3ChampionsStatisticService.Matches;

/// <summary>A distinct (map id, stored map name) pair of the finished matches of one season and game mode.</summary>
public class MapIdAndName
{
    public int? MapId { get; set; }
    public string MapName { get; set; }
}
