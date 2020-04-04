using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.MatchEvents;

namespace W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats
{
    public class Wc3Stats
    {
        public string Id => nameof(Wc3Stats);

        public List<W3StatsPerMode> StatsPerModes { get; set; } = new List<W3StatsPerMode>
        {
            W3StatsPerMode.Create(GameMode.GM_1v1),
            W3StatsPerMode.Create(GameMode.GM_2v2),
            W3StatsPerMode.Create(GameMode.GM_4v4),
            W3StatsPerMode.Create(GameMode.FFA),
        };

        public void Apply(MatchFinishedEvent nextEvent)
        {
            var players = nextEvent.match.players;
            var gameMode = (GameMode) nextEvent.match.gameMode;

            var w3StatsPerMode = StatsPerModes.Single(m => m.GameMode == gameMode);
            w3StatsPerMode.AddWin(players, nextEvent.match.map);
        }
    }
}