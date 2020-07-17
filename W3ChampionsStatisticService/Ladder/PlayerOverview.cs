using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerOverview : WinLoss, IIdentifiable
    {
        public static PlayerOverview Create(
            List<PlayerId> playerIds,
            GateWay gateWay,
            GameMode gameMode,
            int season,
            Race? race)
        {
            var id = $"{season}_{string.Join("_", playerIds.OrderBy(t => t.BattleTag).Select(t => $"{t.BattleTag}@{(int)gateWay}"))}_{gameMode}";
            if (race != null)
            {
                id += $"_{race}";
            }

            return new PlayerOverview
            {
                Id = id,
                PlayerIds = playerIds,
                GateWay = gateWay,
                GameMode = gameMode,
                Season = season,
                Race = race
            };
        }

        public List<PlayerId> PlayerIds { get; set; }

        public string Name => string.Join(" & ", PlayerIds.Select(p => p.Name));
        public string Id { get; set; }
        public int MMR { get; set; }
        public GateWay GateWay { get; set; }
        public GameMode GameMode { get; set; }
        public int Season { get; set; }
        public Race? Race { get; set; }

        public void RecordWin(bool won, int newMmr)
        {
            MMR = newMmr;
            RecordWin(won);
        }
    }
}