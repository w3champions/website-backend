using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class PersonalSettingsRepository : MongoDbRepositoryBase, IPersonalSettingsRepository
    {
        public PersonalSettingsRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public async Task<PersonalSetting> Load(string battletag)
        {
            var settings = CreateCollection<PersonalSetting>();
            var players = CreateCollection<PlayerOverallStats>();
            var result = await settings
                .Aggregate()
                .Match(p => p.Id.ToLower() == battletag.ToLower())
                .Lookup<PersonalSetting, PlayerOverallStats, PersonalSetting>(players,
                    rank => rank.Id,
                    player => player.BattleTag,
                    rank => rank.Players)
                .FirstOrDefaultAsync();
            return result;
        }

        public Task<List<PersonalSetting>> LoadSince(DateTimeOffset from)
        {
            return LoadSince<PersonalSetting>(from);
        }

        public async Task<List<PersonalSetting>> LoadMany(string[] battletags)
        {
            var settings = CreateCollection<PersonalSetting>();
            var results = new List<PersonalSetting>();
            foreach (var tag in battletags)
            {
                var filter = Builders<PersonalSetting>.Filter.Regex("_id", new BsonRegularExpression(tag, "i"));
                var result = await settings.Find(filter).FirstOrDefaultAsync();
                if (result != null)
                {
                    results.Add(result);
                }
            }
            return results;
        }

        public Task<List<PersonalSetting>> LoadAll()
        {
            return LoadAll<PersonalSetting>();
        }

        public Task Save(PersonalSetting setting)
        {
            setting.Players = null;
            return UpsertTimed(setting, p => p.Id == setting.Id);
        }

        public Task SaveMany(List<PersonalSetting> settings)
        {
            return UpsertMany(settings);
        }
    }
}