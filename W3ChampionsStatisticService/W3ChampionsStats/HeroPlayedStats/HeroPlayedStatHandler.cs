using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats;

[Trace]
public class HeroPlayedStatHandler(IW3StatsRepo w3Stats) : IReadModelHandler
{
    private readonly IW3StatsRepo _w3Stats = w3Stats;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        if (nextEvent.WasFakeEvent) return;
        var stat = await _w3Stats.LoadHeroPlayedStat() ?? HeroPlayedStat.Create();
        if (nextEvent.result == null) return;

        var heroes = nextEvent.result.players.SelectMany(p =>
            p.heroes.Select((h, index) => new HeroPickDto(h.icon, (EPick)index + 1))).ToList();
        stat.AddHeroes(heroes, nextEvent.match.gameMode);
        await _w3Stats.Save(stat);
    }
}

public class HeroPickDto(string icon, in EPick pick)
{
    public string Icon { get; } = icon.ParseReforgedName();
    public EPick Pick { get; } = pick;
}
