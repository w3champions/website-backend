using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerOverview : WinLoss
    {
        public static PlayerOverview Create(List<PlayerId> playerIds, int gateWay, GameMode gameMode)
        {
            return new PlayerOverview
            {
                Id = $"{string.Join("_", playerIds.OrderBy(t => t.Id).Select(t => t.Id))}_{gameMode}",
                PlayerIds = playerIds,
                GateWay = gateWay,
                GameMode = gameMode
            };
        }

        public List<PlayerId> PlayerIds { get; set; }

        public string Id { get; set; }
        public int MMR { get; set; }
        public int GateWay { get; set; }
        public GameMode GameMode { get; set; }

        public void RecordWin(bool won, int newMmr)
        {
            MMR = newMmr;
            RecordWin(won);
        }
    }
}