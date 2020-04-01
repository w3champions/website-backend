using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapStats
{
    public class RaceOnMapRatio
    {
        public static RaceOnMapRatio Create(string battleTag)
        {
            return new RaceOnMapRatio
            {
                Id = battleTag
            };
        }

        public RaceWinRatio RaceWinRatio { get; set; } = RaceWinRatio.CreateMapBased();
        public string Id { get; set; }

        public void AddMapWin(bool won, Race myRace, string mapName)
        {
            var winLosses = RaceWinRatio[myRace.ToString()];
            if (!winLosses.ContainsKey(mapName))
            {
                winLosses[mapName] = new WinLoss();
            }

            winLosses[mapName].RecordWin(won);
        }
    }
}