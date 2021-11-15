using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Admin;

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

        private Task<List<PersonalSetting>> UpsertSpecialPortraits(PortraitsRequest portraitsRequest)
        {
            foreach (var battetag in portraitsRequest.BnetTags)
            {

                Upsert<>
            }
        }
    }
}