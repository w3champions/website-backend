using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// Batch-loads win-milestone read-model docs for a set of entity ids and maps them to serve-views,
// keyed by id. Used by the profile (GameModeStatQueryHandler) query path to stamp the milestone
// progress onto the DTO. Ids that have no record are simply absent.
[Trace]
public class MilestoneViewLoader(IProgressionMilestoneRepository repository)
{
    private readonly IProgressionMilestoneRepository _repository = repository;

    public async Task<Dictionary<string, PlayerMilestoneView>> LoadViews(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<string, PlayerMilestoneView>();
        }

        var docs = await _repository.LoadMilestones(ids);
        var views = new Dictionary<string, PlayerMilestoneView>();
        foreach (var doc in docs)
        {
            var view = PlayerMilestoneView.FromReadModel(doc, DateTimeOffset.UtcNow);
            if (view != null)
            {
                views[doc.Id] = view;
            }
        }

        return views;
    }
}
