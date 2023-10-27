using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;

namespace W3C.Domain.CommonValueObjects;

public class PatchRepository : MongoDbRepositoryBase, IPatchRepository
{
    public PatchRepository(MongoClient mongoClient) : base(mongoClient)
    {
    }

    public async Task<string> GetPatchVersionFromDate(DateTime dateTime)
    {
        var patches = await LoadPatches();
        return patches.Where(x => dateTime > x.StartDate).ToList().Last().Version;
    }

    public Task<List<Patch>> LoadPatches()
    {
        return LoadAll<Patch>();
    }

    public Task InsertPatches(List<Patch> patches)
    {
        return UpsertMany(patches);
    }
}

public interface IPatchRepository
{
    Task<string> GetPatchVersionFromDate(DateTime date);

    Task<List<Patch>> LoadPatches();
}
