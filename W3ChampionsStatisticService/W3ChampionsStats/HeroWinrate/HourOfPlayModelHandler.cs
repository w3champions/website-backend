using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Extensions;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate
{
    public class HeroWinRatePerHeroModelHandler : IReadModelHandler
    {
        private readonly IW3StatsRepo _w3Stats;

        public HeroWinRatePerHeroModelHandler(
            IW3StatsRepo w3Stats
            )
        {
            _w3Stats = w3Stats;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            if (nextEvent.result == null || nextEvent.match.gameMode != GameMode.GM_1v1) return;

            var winner = nextEvent.match.players.Single(p => p.won);
            var looser = nextEvent.match.players.Single(p => !p.won);

            var winnerHeroes = nextEvent.result.players.Single(p => p.battleTag == winner.battleTag).heroes;
            var looserHeroes = nextEvent.result.players.Single(p => p.battleTag == looser.battleTag).heroes;

            var heroComboIdWinner = string.Join("_", winnerHeroes.Select(h => h.icon.ParseReforgedName()));
            var heroComboIdLooser = string.Join("_", looserHeroes.Select(h => h.icon.ParseReforgedName()));

            var winnerWinrate = await _w3Stats.LoadHeroWinrate(heroComboIdWinner);
            var looserWinrate = await _w3Stats.LoadHeroWinrate(heroComboIdLooser);

            winnerWinrate.RecordGame(true, heroComboIdLooser);
            looserWinrate.RecordGame(false, heroComboIdLooser);

            await _w3Stats.Save(winnerWinrate);
            await _w3Stats.Save(looserWinrate);
        }
    }
}