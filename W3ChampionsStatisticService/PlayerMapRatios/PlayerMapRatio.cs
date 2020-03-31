using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerRaceLossRatios;

namespace W3ChampionsStatisticService.PlayerMapRatios
{
    public class PlayerMapRatio
    {
        public static PlayerMapRatio Create(string battleTag)
        {
            return new PlayerMapRatio
            {
                Id = battleTag
            };
        }

        public RaceWinRatio RaceWinRatio { get; set; } = RaceWinRatio.CreateRaceBased();
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