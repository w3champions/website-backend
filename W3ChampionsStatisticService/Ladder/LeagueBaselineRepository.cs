using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Ladder;

[Trace]
public class LeagueBaselineRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), ILeagueBaselineRepository
{
    public Task<List<LeagueBaseline>> LoadByIds(List<string> ids)
    {
        return LoadAll<LeagueBaseline>(b => ids.Contains(b.Id));
    }

    public Task UpsertMany(List<LeagueBaseline> baselines)
    {
        foreach (var baseline in baselines)
        {
            baseline.LastUpdated = DateTimeOffset.UtcNow;
        }
        return base.UpsertMany(baselines);
    }
}
