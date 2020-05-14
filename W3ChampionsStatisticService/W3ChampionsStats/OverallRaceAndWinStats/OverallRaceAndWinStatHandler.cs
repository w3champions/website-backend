using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats
{
    public class OverallRaceAndWinStatHandler : IReadModelHandler
    {
        private readonly IW3StatsRepo _w3Stats;

        public OverallRaceAndWinStatHandler(
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

                var statOverall = await _w3Stats.LoadRaceVsRaceStat(0) ?? new OverallRaceAndWinStat(0);
                var statPerMmr = await _w3Stats.LoadRaceVsRaceStat(ToMaxMMr(averageMmr)) ?? new OverallRaceAndWinStat(ToMaxMMr(averageMmr));

                statOverall.Apply("Overall", players[0].race, players[1].race, players[0].won);
                statOverall.Apply("Overall", players[1].race, players[0].race, players[1].won);

                statOverall.Apply(new MapName(nextEvent.match.map).Name, players[0].race, players[1].race, players[0].won);
                statOverall.Apply(new MapName(nextEvent.match.map).Name, players[1].race, players[0].race, players[1].won);

                statPerMmr.Apply("Overall", players[0].race, players[1].race, players[0].won);
                statPerMmr.Apply("Overall", players[1].race, players[0].race, players[1].won);

                statPerMmr.Apply(new MapName(nextEvent.match.map).Name, players[0].race, players[1].race, players[0].won);
                statPerMmr.Apply(new MapName(nextEvent.match.map).Name, players[1].race, players[0].race, players[1].won);

                await _w3Stats.Save(statOverall);
                await _w3Stats.Save(statPerMmr);
            }
        }

        private int ToMaxMMr(double averageMmr)
        {
            if (averageMmr > 2200) return 2200;
            if (averageMmr > 2000) return 2000;
            if (averageMmr > 1800) return 1800;
            if (averageMmr > 1600) return 1600;
            if (averageMmr > 1400) return 1400;
            if (averageMmr > 1200) return 1200;
            return 1000;
        }
    }
}