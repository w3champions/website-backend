using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents.PadSync;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Admin
{
    public class BanSyncHandler : IAsyncUpdatable
    {
        private readonly PadServiceRepo _padRepo;
        private readonly BanReadmodelRepository _banRepository;

        public BanSyncHandler(
            PadServiceRepo padRepo,
            BanReadmodelRepository banRepository
            )
        {
            _padRepo = padRepo;
            _banRepository = banRepository;
        }

        public async Task Update()
        {
            var bans = await _padRepo.GetBannedPlayers();
            await _banRepository.UpdateBans(bans.players);
        }
    }
}