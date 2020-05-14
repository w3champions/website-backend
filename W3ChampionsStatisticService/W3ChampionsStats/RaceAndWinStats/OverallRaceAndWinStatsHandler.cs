using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats
{
    public class OverallRaceAndWinStatsHandler : IReadModelHandler
    {
        private readonly IW3StatsRepo _w3Stats;

        public OverallRaceAndWinStatsHandler(
            IW3StatsRepo w3Stats
            )
        {
            _w3Stats = w3Stats;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            if (nextEvent.WasFakeEvent
                ||nextEvent.match.players.All(p => p.won)
                ||nextEvent.match.players.All(p => !p.won)) return;

            if (nextEvent.match.gameMode == GameMode.GM_1v1)
            {
                var players = nextEvent.match.players;
                var averageMmr = players.Average(p => p.mmr.rating);

                var statOverall = await _w3Stats.Load() ?? new OverallRaceAndWinStats(-1);
                var statPerMmr = await _w3Stats.Load() ?? new OverallRaceAndWinStats(ToLeagueOrder(averageMmr));

                statOverall.Apply("Overall", players[0].race, players[1].race, players[0].won);
                statOverall.Apply("Overall", players[1].race, players[0].race, players[1].won);

                statPerMmr.Apply(new MapName(nextEvent.match.map).Name, players[0].race, players[1].race, players[0].won);
                statPerMmr.Apply(new MapName(nextEvent.match.map).Name, players[1].race, players[0].race, players[1].won);

                await _w3Stats.Save(statOverall);
                await _w3Stats.Save(statPerMmr);
            }
        }

        private int ToLeagueOrder(double averageMmr)
        {
            if (averageMmr > 2200) return 0;
            if (averageMmr > 1800) return 1;
            if (averageMmr > 1600) return 2;
            if (averageMmr > 1400) return 3;
            if (averageMmr > 1200) return 4;
            if (averageMmr > 1000) return 5;
            return 6;
        }
    }
}