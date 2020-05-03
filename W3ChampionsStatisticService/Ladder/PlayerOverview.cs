using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerOverview
    {
        public PlayerOverview(string id, string nameTag, int gateWay)
        {
            Id = id;
            GateWay = gateWay;
            Name = nameTag.Split("#")[0];
            BattleTag = nameTag.Split("#")[1];
        }

        public string Id { get; set; }
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public int TotalLosses { get; set; }
        public int TotalWins { get; set; }
        public int Games => TotalLosses + TotalWins;
        public double Winrate => new WinRate(TotalWins, TotalLosses).Rate;
        public int MMR { get; set; }
        public int GateWay { get; set; }
        public List<GameModeWinLoss> WinsByMode { get; set; } = new List<GameModeWinLoss>
        {
            GameModeWinLoss.Create(GameMode.GM_1v1),
            GameModeWinLoss.Create(GameMode.GM_2v2_AT),
            GameModeWinLoss.Create(GameMode.GM_4v4),
            GameModeWinLoss.Create(GameMode.FFA),
        };

        public void RecordWin(bool won, int newMmr, GameMode gameMode)
        {
            var gameModeWinLoss = WinsByMode.Single(m => m.GameMode == gameMode);
            gameModeWinLoss.RecordWin(won);
            MMR = newMmr;
            if (won)
            {
                TotalWins++;
            }
            else
            {
                TotalLosses++;
            }
        }
    }

    public class GameModeWinLoss : WinLoss
    {
        public static GameModeWinLoss Create(GameMode gameMode)
        {
            return new GameModeWinLoss {GameMode = gameMode};
        }

        public GameMode GameMode { get; set; }
    }
}