using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PersonalSettings.Hotkeys;

[Trace]
public class HotkeySetupRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IHotkeySetupRepository
{
    public Task<HotkeySetup> Load(string battleTag)
    {
        return LoadFirst<HotkeySetup>(battleTag);
    }

    public async Task<bool> TryCreate(HotkeySetup setup)
    {
        try
        {
            await Insert(setup);
            return true;
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<bool> TryReplace(HotkeySetup setup, long expectedRevision)
    {
        var result = await CreateCollection<HotkeySetup>()
            .ReplaceOneAsync(x => x.Id == setup.Id && x.Revision == expectedRevision, setup);
        return result.MatchedCount == 1;
    }
}
