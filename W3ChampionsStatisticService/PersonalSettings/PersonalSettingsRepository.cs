using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

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

            if (result != null && SchemaOutdated(result))
            {
                var settingList = new List<PersonalSetting>();
                settingList.Add(result);
                await UpdateSchema(settingList);
                result = await Load(battletag);
            }
            return result;
        }

        public Task<List<PersonalSetting>> LoadSince(DateTimeOffset from)
        {
            return LoadSince<PersonalSetting>(from);
        }

        public async Task<List<PersonalSetting>> LoadMany(string[] battletags)
        {
            var settings = CreateCollection<PersonalSetting>();
            var tags = String.Join("|", battletags);
            var filter = Builders<PersonalSetting>.Filter.Regex("_id", new BsonRegularExpression(tags, "i"));
            var results = await settings.Find(filter).ToListAsync();
            //await UpdateSchema(results);
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

        public Task UnsetOne(string fieldName, string battleTag)
        {
            return UnsetOne<PersonalSetting>(fieldName, battleTag);
        }

        public async Task UpdateSchema(List<PersonalSetting> settings)
        {
            var updatedSettings = settings;
            List<PersonalSetting> outOfDateDocuments = new();
            foreach (var setting in settings)
            {
                if (!setting.ToBsonDocument().Contains("SpecialPictures"))
                {
                    setting.SpecialPictures = Array.Empty<SpecialPicture>();
                    outOfDateDocuments.Add(setting);
                }
            }

            await SaveMany(outOfDateDocuments);
            return;
        }

        public bool SchemaOutdated(PersonalSetting setting)
        {
            if (!setting.ToBsonDocument().Contains("SpecialPictures"))
            {
                return true;
            }
            return false;
        }
    }
}