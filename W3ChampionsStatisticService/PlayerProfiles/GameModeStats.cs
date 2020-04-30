using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class GameModeStats : List<GameModeStat>
    {
        public void RecordGame(GameMode mode, bool won)
        {
            var gameModeStat = this.Single(s => s.Mode == mode);
            gameModeStat.Update(won);
        }

        public void RecordRanking(GameMode mode, in int mmr, in int rankingPoints)
        {
            var gameModeStat = this.Single(s => s.Mode == mode);
            gameModeStat.Update(mmr, rankingPoints);
        }
    }
}