using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.Services
{
    public class PersonalSettingsProvider : MongoDbRepositoryBase
    {
        private readonly ICacheData<List<PersonalSetting>> _cacheData;

        public PersonalSettingsProvider(MongoClient mongoClient, ICacheData<List<PersonalSetting>> cacheData) : base(mongoClient)
        {
            _cacheData = cacheData;
        }

        public Task<List<PersonalSetting>> GetPersonalSettingsAsync()
        {
            return _cacheData.GetCachedOrRequestAsync(FetchPersonalSettingsAsync, null);
        }

        private Task<List<PersonalSetting>> FetchPersonalSettingsAsync()
        {
            return LoadAll<PersonalSetting>();
        }
    }
}