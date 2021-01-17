using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq.Expressions;
using MongoDB.Driver;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Services
{
    public class PersonalSettingsProvider : MongoDbRepositoryBase
    {
        PersonalSettingsProvider(MongoClient mongoClient) : base(mongoClient)
        {
        }

        private static CachedData<List<PersonalSettings.PersonalSetting>> personalSettingsCache = new CachedData<List<PersonalSettings.PersonalSetting>>(() => FetchPersonalSettingsSync(), TimeSpan.FromMinutes(10));
        
        public CachedData<List<PersonalSettings.PersonalSetting>> getPersonalSettingsCache()
        {
            return personalSettingsCache;
        }

        private static List<PersonalSettings.PersonalSetting> FetchPersonalSettingsSync()
        {
            try 
            {
                return FetchPersonalSettings().GetAwaiter().GetResult();
            }
            catch
            {
                return new List<PersonalSettings.PersonalSetting>();
            }
        }

        private static Task<List<PersonalSettings.PersonalSetting>> FetchPersonalSettings()
        {
            return LoadAll<PersonalSettings.PersonalSetting>();
        }
    
    }
}