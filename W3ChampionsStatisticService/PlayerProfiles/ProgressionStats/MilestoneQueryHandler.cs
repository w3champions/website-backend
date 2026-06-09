using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// Owner-private milestone read path: loads every lifetime win-milestone doc the caller participates in
// (their solo docs + any arranged-team doc they are a member of) and maps each to a flat MilestoneDto.
// The battleTag comes from the caller's JWT (never the path), so the result is the caller's own data only.
[Trace]
public class MilestoneQueryHandler(IProgressionMilestoneRepository repository)
{
    private readonly IProgressionMilestoneRepository _repository = repository;

    public async Task<List<MilestoneDto>> LoadForPlayer(string battleTag)
    {
        var docs = await _repository.LoadMilestonesForPlayer(battleTag);
        var now = DateTimeOffset.UtcNow;
        return docs.Select(doc => MilestoneDto.FromReadModel(doc, now)).ToList();
    }
}
