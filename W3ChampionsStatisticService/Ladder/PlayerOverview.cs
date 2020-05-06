using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerOverview : WinLoss
    {
        public static PlayerOverview Create(List<PlayerId> playerIds, GateWay gateWay, GameMode gameMode)
        {
            return new PlayerOverview
            {
                Id = $"{string.Join("_", playerIds.OrderBy(t => t.BattleTag).Select(t => $"{t.BattleTag}@{(int)gateWay}"))}_{gameMode}",
                PlayerIds = playerIds,
                GateWay = gateWay,
                GameMode = gameMode
            };
        }

        public List<PlayerId> PlayerIds { get; set; }

        public string Name => string.Join(" & ", PlayerIds.Select(p => p.Name));
        public string Id { get; set; }
        public int MMR { get; set; }
        public GateWay GateWay { get; set; }
        public GameMode GameMode { get; set; }

        public void RecordWin(bool won, int newMmr)
        {
            MMR = newMmr;
            RecordWin(won);
        }
    }
}