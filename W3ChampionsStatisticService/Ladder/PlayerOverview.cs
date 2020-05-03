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
                // Todo remove when pads events are correct
                Id = gameMode != GameMode.GM_1v1
                    ? $"{string.Join("_", playerIds.OrderBy(t => t.Id).Select(t => t.Id))}_{gameMode}"
                    : string.Join("_", playerIds.OrderBy(t => t.Id).Select(t => t.Id)),
                PlayerIds = playerIds,
                GateWay = gateWay,
                GameMode = gameMode
            };
        }

        public List<PlayerId> PlayerIds { get; set; }

        public string Name => string.Join(" & ", PlayerIds.Select(p => p.Name));
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