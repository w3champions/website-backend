using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PadEvents.PadSync
{
    public class PadLeagueSyncHandler : IAsyncUpdatable
    {
        private readonly IMatchEventRepository _matchEventRepository;
        private readonly IPadServiceRepo _padRepo;
        private readonly IRankRepository _rankRepository;

        public PadLeagueSyncHandler(
            IPadServiceRepo padRepo,
            IRankRepository rankRepository,
            IMatchEventRepository matchEventRepository
        )
        {
            _padRepo = padRepo;
            _rankRepository = rankRepository;
            _matchEventRepository = matchEventRepository;
        }

        public async Task Update()
        {
            var loadLeagueConstellation = await _matchEventRepository.LoadLeagueConstellationChanged();

            var leagueConstellations = loadLeagueConstellation.Select(l =>
                new LeagueConstellation(l.season, l.gateway, l.gameMode, l.leagues.Select(le =>
                    new League(le.id, le.order, le.name.Replace("League", "").Trim(), le.division)
                ).OrderBy(l => l.Order).ThenBy(l => l.Division).ToList().ToList())
            ).ToList();

            await _rankRepository.InsertLeagues(leagueConstellations);

            await Task.Delay(60000);
        }
    }
}