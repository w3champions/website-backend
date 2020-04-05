using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats
{
    public class Wc3StatsModelHandler : IReadModelHandler
    {
        private readonly IW3StatsRepo _w3Stats;

        public Wc3StatsModelHandler(
            IW3StatsRepo w3Stats
            )
        {
            _w3Stats = w3Stats;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            if ((GameMode) nextEvent.match.gameMode == GameMode.GM_1v1)
            {
                var stat = await _w3Stats.Load() ?? new Wc3Stats();
                var players = nextEvent.match.players;

                stat.Apply("Overall", (Race) players[0].race, (Race) players[1].race, players[0].won);
                stat.Apply("Overall", (Race) players[1].race, (Race) players[0].race, players[1].won);

                stat.Apply(nextEvent.match.map, (Race) players[0].race, (Race) players[1].race, players[0].won);
                stat.Apply(nextEvent.match.map, (Race) players[1].race, (Race) players[0].race, players[1].won);

                await _w3Stats.Save(stat);
            }
        }
    }
}