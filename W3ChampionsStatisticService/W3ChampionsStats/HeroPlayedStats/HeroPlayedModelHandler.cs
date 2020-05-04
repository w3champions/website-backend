using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats
{
    public class HeroPlayedModelHandler : IReadModelHandler
    {
        private readonly IW3StatsRepo _w3Stats;

        public HeroPlayedModelHandler(
            IW3StatsRepo w3Stats
            )
        {
            _w3Stats = w3Stats;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            var stat = await _w3Stats.LoadHeroPlayedStat() ?? HeroPlayedStat.Create();
            if (nextEvent.result == null) return;

            var heroes = nextEvent.result.players.SelectMany(p => p.heroes.Select(
                    (h, index) => new HeroPickDto(h.icon, index)))
            .ToList();
            stat.AddHeroes(heroes, nextEvent.match.gameMode);
            await _w3Stats.Save(stat);
        }
    }

    public class HeroPickDto
    {
        public string Icon { get; }
        public int Pick { get; }

        public HeroPickDto(string icon, in int pick)
        {
            Icon = ParseReforgedName(icon);;
            Pick = pick;
        }

        private string ParseReforgedName(string heroIcon)
        {
            if (heroIcon == "jainasea") return "archmage";
            if (heroIcon == "thrallchampion") return "farseer";
            if (heroIcon == "fallenkingarthas") return "deathknight";
            if (heroIcon == "cenariusnightmare") return "keeperofthegrove";
            return heroIcon;
        }
    }
}