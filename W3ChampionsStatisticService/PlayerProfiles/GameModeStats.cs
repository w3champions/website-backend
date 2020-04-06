using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class GameModeStats : List<GameModeStat>
    {
        public void RecordGame(GameMode mode, bool won, int mmr)
        {
            var gameModeStat = this.Single(s => s.Mode == mode);
            gameModeStat.Update(won, mmr);
        }
    }
}