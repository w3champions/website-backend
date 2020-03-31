using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerRaceLossRatios;

namespace W3ChampionsStatisticService.PlayerMapAndRaceRatios
{
    public class PlayerMapAndRaceRatio
    {
        public static PlayerMapAndRaceRatio Create(string battleTag)
        {
            return new PlayerMapAndRaceRatio
            {
                Id = battleTag
            };
        }

        public RaceWinAndMapRatio RaceWinRatio { get; set; } = RaceWinAndMapRatio.Create();
        public string Id { get; set; }

        public void AddMapWin(bool won, Race myRace, Race enemyRace, string mapName)
        {
            var winLosses = RaceWinRatio[myRace.ToString()];
            if (!winLosses.ContainsKey(mapName))
            {
                winLosses[mapName] = new Dictionary<string, WinLoss>
                {
                    { Race.HU.ToString(), new WinLoss() },
                    { Race.OC.ToString(), new WinLoss() },
                    { Race.NE.ToString(), new WinLoss() },
                    { Race.UD.ToString(), new WinLoss() }
                };
            }

            winLosses[mapName][enemyRace.ToString()].RecordWin(won);
        }
    }
}