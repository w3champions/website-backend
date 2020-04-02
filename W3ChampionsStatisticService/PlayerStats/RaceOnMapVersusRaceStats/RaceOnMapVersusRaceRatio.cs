using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats
{
    public class RaceOnMapVersusRaceRatio
    {
        public static RaceOnMapVersusRaceRatio Create(string battleTag)
        {
            return new RaceOnMapVersusRaceRatio
            {
                Id = battleTag
            };
        }

        public RaceWinRatio RaceWinRatio { get; set; } = RaceWinRatio.Create();
        public string Id { get; set; }

        public void AddMapWin(bool won, Race myRace, Race enemyRace, string mapName)
        {
            var winLosses = RaceWinRatio[myRace.ToString()];
            if (!winLosses.ContainsKey(mapName))
            {
                winLosses[mapName] = new Dictionary<string, WinLoss>
                {
                    { Race.RnD.ToString(), new WinLoss() },
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