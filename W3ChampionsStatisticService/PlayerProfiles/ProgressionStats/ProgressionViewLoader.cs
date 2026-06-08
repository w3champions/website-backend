using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// Batch-loads progression read-model docs for a set of entity ids and maps them to serve-views,
// keyed by id. Shared by the ladder (RankQueryHandler) and profile (GameModeStatQueryHandler)
// query paths so both stamp the DTO the same way. Ids that have no record are simply absent.
[Trace]
public class ProgressionViewLoader(IPlayerProgressionRepository repository)
{
    private readonly IPlayerProgressionRepository _repository = repository;

    public async Task<Dictionary<string, PlayerProgressionView>> LoadViews(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<string, PlayerProgressionView>();
        }

        var docs = await _repository.LoadProgressions(ids);
        return docs.ToDictionary(d => d.Id, PlayerProgressionView.FromReadModel);
    }
}
