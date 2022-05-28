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
        public static CachedData<List<PersonalSetting>> personalSettingsCache;

        public PersonalSettingsProvider(MongoClient mongoClient) : base(mongoClient)
        {
            personalSettingsCache = new CachedData<List<PersonalSetting>>(() => FetchPersonalSettingsSync(), TimeSpan.FromMinutes(10));
        }
        
        public List<PersonalSetting> GetPersonalSettings()
        {
            try 
            {
                return personalSettingsCache.GetCachedData();
            }
            catch
            {
                return new List<PersonalSetting>();
            }
        }

        private List<PersonalSetting> FetchPersonalSettingsSync()
        {
            try 
            {
                return FetchPersonalSettings().GetAwaiter().GetResult();
            }
            catch
            {
                return new List<PersonalSetting>();
            }
        }

        private Task<List<PersonalSetting>> FetchPersonalSettings()
        {
            return LoadAll<PersonalSetting>();
        }
    }
}
