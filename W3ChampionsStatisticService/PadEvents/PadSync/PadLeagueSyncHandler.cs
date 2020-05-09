using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PadEvents.PadSync
{
    public class PadLeagueSyncHandler : IAsyncUpdatable
    {
        private readonly IPadServiceRepo _padRepo;
        private readonly IRankRepository _rankRepository;
        private readonly IMatchEventRepository _matchEventRepository;

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

            var leagueConstellations = loadLeagueConstellation.Select(l => new LeagueConstellation
            {
                GameMode = l.gameMode,
                Gateway = l.gateway,
                Id = $"{l.gateway}_{l.gameMode}",
                Leagues = l.leagues.Select(le => new League
                {
                    Id = le.id,
                    Division = le.division,
                    Name = le.name.Replace("League", "").Trim(),
                    Order = le.order
                }).ToList()
            }).ToList();

            await _rankRepository.InsertLeagues(leagueConstellations);

            await Task.Delay(86400000);
        }
    }
}