using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace W3ChampionsStatisticService.Ports;

public interface IPlayerProgressionRepository
{
    Task<PlayerProgression> LoadProgression(string id);
    Task UpsertProgression(PlayerProgression progression);
    Task<List<PlayerProgression>> LoadProgressions(IReadOnlyList<string> ids);
}
