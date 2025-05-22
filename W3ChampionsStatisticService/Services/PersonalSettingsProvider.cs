using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.PersonalSettings;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Services;

// TODO: It is a repository with a cache
[Trace]
public class PersonalSettingsProvider(MongoClient mongoClient, ICachedDataProvider<List<PersonalSetting>> cachedDataProvider) : MongoDbRepositoryBase(mongoClient)
{
    private readonly ICachedDataProvider<List<PersonalSetting>> _cachedDataProvider = cachedDataProvider;

    public Task<List<PersonalSetting>> GetPersonalSettingsAsync()
    {
        return _cachedDataProvider.GetCachedOrRequestAsync(FetchPersonalSettingsAsync, null);
    }

    private Task<List<PersonalSetting>> FetchPersonalSettingsAsync()
    {
        return LoadAll<PersonalSetting>();
    }
}
