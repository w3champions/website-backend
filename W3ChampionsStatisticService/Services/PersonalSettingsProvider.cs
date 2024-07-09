using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.Services;

// TODO: It is a repository with a cache
public class PersonalSettingsProvider : MongoDbRepositoryBase
{
    private readonly ICachedDataProvider<List<PersonalSetting>> _cachedDataProvider;

    public PersonalSettingsProvider(MongoClient mongoClient, ICachedDataProvider<List<PersonalSetting>> cachedDataProvider) : base(mongoClient)
    {
        _cachedDataProvider = cachedDataProvider;
    }

    public Task<List<PersonalSetting>> GetPersonalSettingsAsync()
    {
        return _cachedDataProvider.GetCachedOrRequestAsync(FetchPersonalSettingsAsync, null);
    }

    private Task<List<PersonalSetting>> FetchPersonalSettingsAsync()
    {
        return LoadAll<PersonalSetting>();
    }
}
