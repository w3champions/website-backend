using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Ladder;

[Trace]
public class LeagueSyncHandler(
    IRankRepository rankRepository,
    IMatchEventRepository matchEventRepository
    ) : IAsyncUpdatable
{
    private readonly IMatchEventRepository _matchEventRepository = matchEventRepository;
    private readonly IRankRepository _rankRepository = rankRepository;

    public async Task Update()
    {
        var loadLeagueConstellation = await _matchEventRepository.LoadLeagueConstellationChanged();

        var leagueConstellations = loadLeagueConstellation.Select(l =>
            new LeagueConstellation(l.season, l.gateway, l.gameMode, l.leagues.Select(le =>
                new League(le.id, le.order, le.name.Replace("League", "").Trim(), le.division)
            ).OrderBy(l => l.Order).ThenBy(l => l.Division).ToList().ToList())
        ).ToList();

        await _rankRepository.InsertLeagues(leagueConstellations);
        if (leagueConstellations.Any())
        {
            await _rankRepository.UpsertSeason(new Season(leagueConstellations.Max(l => l.Season)));
        }
    }
}
