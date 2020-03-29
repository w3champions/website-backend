using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Players
{
    public class GameModeStats : List<GameModeStat>
    {
        public GameModeStats()
        {
            Add(new GameModeStat(GameMode.GM_1v1));
            Add(new GameModeStat(GameMode.GM_2v2));
            Add(new GameModeStat(GameMode.GM_4v4));
            Add(new GameModeStat(GameMode.FFA));
        }

        public void RecordGame(GameMode mode, bool won)
        {
            var gameModeStat = this.Single(s => s.Mode == mode);
            gameModeStat.Update(won);
        }
    }
}