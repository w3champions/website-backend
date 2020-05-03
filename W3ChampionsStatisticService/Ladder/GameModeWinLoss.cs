using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.Ladder
{
    public class GameModeWinLoss : WinLoss
    {
        public static GameModeWinLoss Create(GameMode gameMode)
        {
            return new GameModeWinLoss {GameMode = gameMode};
        }

        public GameMode GameMode { get; set; }
    }
}