using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PadEvents.PadSync
{
    public class PadLeagueSyncHandler : IAsyncUpdatable
    {
        private readonly IPadServiceRepo _padRepo;
        private readonly IRankRepository _rankRepository;

        public PadLeagueSyncHandler(
            IPadServiceRepo padRepo,
            IRankRepository rankRepository
            )
        {
            _padRepo = padRepo;
            _rankRepository = rankRepository;
        }

        public async Task Update()
        {
            var eu1V1 = await _padRepo.GetLeague(GateWay.Europe, GameMode.GM_1v1);
            var us1V1 = await _padRepo.GetLeague(GateWay.Usa, GameMode.GM_1v1);

            var eu2V2 = await _padRepo.GetLeague(GateWay.Europe, GameMode.GM_2v2_AT);
            var us2V2 = await _padRepo.GetLeague(GateWay.Usa, GameMode.GM_2v2_AT);

            ReplaceLagueInName(eu1V1);
            ReplaceLagueInName(us1V1);
            ReplaceLagueInName(eu2V2);
            ReplaceLagueInName(us2V2);

            await _rankRepository.InsertLeague(eu1V1);
            await _rankRepository.InsertLeague(us1V1);
            await _rankRepository.InsertLeague(eu2V2);
            await _rankRepository.InsertLeague(us2V2);

            await Task.Delay(86400000);
        }

        private void ReplaceLagueInName(LeagueConstellation constellation)
        {
            foreach (var league in constellation.Leagues)
            {
                league.Name = league.Name.Replace("League", "").Trim();
            }
        }
    }
}