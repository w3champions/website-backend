using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Admin
{
    public class AdminCommandHandler
    {
        private readonly IVersionRepository _versionRepository;
        private readonly IAdminRepository _adminRepository;

        public AdminCommandHandler(
            IVersionRepository versionRepository,
            IAdminRepository adminRepository)
        {
            _versionRepository = versionRepository;
            _adminRepository = adminRepository;
        }

        public async Task ResetReadModel(string readModelType, string readModelHandler)
        {
            await _versionRepository.ResetVersion(readModelHandler);
            await _adminRepository.Reset(readModelType);
        }
    }
}