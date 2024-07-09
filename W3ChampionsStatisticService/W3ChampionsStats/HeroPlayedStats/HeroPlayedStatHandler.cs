using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats;

public class HeroPlayedStatHandler : IReadModelHandler
{
    private readonly IW3StatsRepo _w3Stats;

    public HeroPlayedStatHandler(
        IW3StatsRepo w3Stats
        )
    {
        _w3Stats = w3Stats;
    }

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        if (nextEvent.WasFakeEvent) return;
        var stat = await _w3Stats.LoadHeroPlayedStat() ?? HeroPlayedStat.Create();
        if (nextEvent.result == null) return;

        var heroes = nextEvent.result.players.SelectMany(p =>
                p.heroes.Select((h, index) => new HeroPickDto(h.icon, (EPick) index + 1))).ToList();
        stat.AddHeroes(heroes, nextEvent.match.gameMode);
        await _w3Stats.Save(stat);
    }
}

public class HeroPickDto
{
    public string Icon { get; }
    public EPick Pick { get; }

    public HeroPickDto(string icon, in EPick pick)
    {
        Icon = icon.ParseReforgedName();;
        Pick = pick;
    }
}
