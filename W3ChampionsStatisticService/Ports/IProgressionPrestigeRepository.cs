using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace W3ChampionsStatisticService.Ports;

public interface IProgressionPrestigeRepository
{
    Task<ProgressionPrestige> LoadPrestige(string battleTag);
    Task UpsertPrestige(ProgressionPrestige prestige);
}
