using System.Linq;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Players
{
    public class Player : Identifiable
    {
        public string BattleTag { get; set; }
        public RaceStats RaceStats { get; set; }
        public GameModeStats GameModeStats { get; set; }

        public void RecordWin(Race race, GameMode mode, bool won)
        {
            GameModeStats.RecordGame(mode, won);
            RaceStats.RecordGame(race, won);
        }

        public int TotalLosses => GameModeStats.Sum(g => g.Losses);

        public int TotalWins => GameModeStats.Sum(g => g.Wins);
    }
}