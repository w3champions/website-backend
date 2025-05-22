using MongoDB.Driver;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Rewards.Portraits;

[Trace]
public class PortraitRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IPortraitRepository
{
    public Task<List<PortraitDefinition>> LoadPortraitDefinitions()
    {
        return LoadAll<PortraitDefinition>();
    }

    public async Task SaveNewPortraitDefinitions(List<int> _ids, List<string> _group = null)
    {
        var existingPortraits = await LoadPortraitDefinitions();
        var toAdd = _ids.Distinct().ToList();
        foreach (var id in toAdd)
        {
            if (!existingPortraits.Any(x => x.Id == id.ToString()))
            {
                await Insert(new PortraitDefinition(id, _group ?? new List<string>()));
            }
        }
    }

    public async Task DeletePortraitDefinitions(List<int> _ids)
    {
        var existingPortraits = await LoadPortraitDefinitions();
        var toDelete = _ids.Distinct().ToList();
        foreach (var id in toDelete)
        {
            if (existingPortraits.Any(x => x.Id == id.ToString()))
            {
                await Delete<PortraitDefinition>(n => n.Id == id.ToString());
            }
        }
    }

    public async Task UpdatePortraitDefinition(List<int> _ids, List<string> _group)
    {
        var existingPortraits = await LoadPortraitDefinitions();
        var toUpdate = _ids.Distinct().ToList();
        foreach (var id in toUpdate)
        {
            if (existingPortraits.Any(x => x.Id == id.ToString()))
            {
                await Upsert(new PortraitDefinition(id, _group));
            }
        }
    }

    public async Task<List<PortraitGroup>> LoadDistinctPortraitGroups()
    {
        var mongoCollection = CreateCollection<PortraitDefinition>();

        var pipeline = await mongoCollection
            .Aggregate()
            .Match(p => p.Groups.Count > 0)
            .Unwind<PortraitDefinition, SinglePortraitDefinitionAndGroup>(p => p.Groups)
            .ToListAsync();

        var grouped = pipeline.GroupBy(p => p.Groups).Select(p => new PortraitGroup
        {
            Group = p.Key,
            PortraitIds = p.Select(x => x.getId()).ToList(),

        }).ToList();

        return grouped;
    }
}
