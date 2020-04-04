namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapStats
{
    public class MapWinLoss : WinLoss
    {
        public MapWinLoss(string map)
        {
            Map = map;
        }

        public string Map { get; set; }
    }
}