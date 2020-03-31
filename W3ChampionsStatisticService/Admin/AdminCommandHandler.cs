using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Admin
{
    public class AdminCommandHandler
    {
        private readonly IVersionRepository _versionRepository;

        public AdminCommandHandler(
            IVersionRepository versionRepository
            )
        {
            _versionRepository = versionRepository;
        }

        public async Task ResetReadModel(string readModelHandler)
        {
            await _versionRepository.ResetVersion(readModelHandler);
        }
    }
}