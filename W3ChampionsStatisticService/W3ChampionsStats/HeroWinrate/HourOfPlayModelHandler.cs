using System;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate;

[Trace]
public class OverallHeroWinRatePerHeroModelHandler(IW3StatsRepo w3Stats) : IReadModelHandler
{
    private readonly IW3StatsRepo _w3Stats = w3Stats;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        if (nextEvent.result == null
            || nextEvent.match.gameMode != GameMode.GM_1v1
            || nextEvent.match.players.All(p => p.won)
            || nextEvent.match.players.All(p => !p.won)
            || nextEvent.result.players.Count != 2
            || nextEvent.result.players.Any(p => p.heroes.Count == 0)) return;

        var heroComboIdWinner = ExtractHeroComboId(nextEvent, p => p.won);
        var heroComboIdLooser = ExtractHeroComboId(nextEvent, p => !p.won);

        await UpdateStat(heroComboIdWinner, heroComboIdLooser, true);
        await UpdateStat(heroComboIdLooser, heroComboIdWinner, false);
    }

    private static string ExtractHeroComboId(MatchFinishedEvent nextEvent, Func<PlayerBlizzard, bool> func)
    {
        var winnerHeroes = nextEvent.result.players.Single(func).heroes;
        var heroComboIdWinner = string.Join("_", winnerHeroes.Select(h => h.icon.ParseReforgedName()));
        return heroComboIdWinner;
    }

    private async Task UpdateStat(string heroComboIdWinner, string heroComboIdLooser, bool won)
    {
        var winnerWinrate = await _w3Stats.LoadHeroWinrate(heroComboIdWinner) ??
                            OverallHeroWinRatePerHero.Create(heroComboIdWinner);
        winnerWinrate.RecordGame(won, heroComboIdLooser);
        await _w3Stats.Save(winnerWinrate);
    }
}
