using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerOverview : WinLoss
    {
        public static PlayerOverview Create(List<PlayerId> playerIds, GateWay gateWay, GameMode gameMode, int season)
        {
            return new PlayerOverview
            {
                Id = $"{season}_{string.Join("_", playerIds.OrderBy(t => t.BattleTag).Select(t => $"{t.BattleTag}@{(int)gateWay}"))}_{gameMode}",
                PlayerIds = playerIds,
                GateWay = gateWay,
                GameMode = gameMode,
                Season = season
            };
        }

        public List<PlayerId> PlayerIds { get; set; }

        public string Name => string.Join(" & ", PlayerIds.Select(p => p.Name));
        public string Id { get; set; }
        public int MMR { get; set; }
        public GateWay GateWay { get; set; }
        public GameMode GameMode { get; set; }
        public int Season { get; set; }

        public void RecordWin(bool won, int newMmr)
        {
            MMR = newMmr;
            RecordWin(won);
        }
    }
}