using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
                .Match(p => p.Id == battletag)
                .Lookup<PersonalSetting, PlayerOverallStats, PersonalSetting>(players,
                    rank => rank.Id,
                    player => player.BattleTag,
                    rank => rank.Players)
                .FirstOrDefaultAsync();
            return result;
        }

        public async Task<PersonalSetting> Upvote(string battleTag)
        {
            var settings = CreateCollection<PersonalSetting>();
            return await settings.FindOneAndUpdateAsync(s => s.Id == battleTag, Builders<PersonalSetting>.Update.Inc(s => s.Upvotes, 1));
        }

        public Task<List<PersonalSetting>> LoadSince(DateTimeOffset from)
        {
            return LoadSince<PersonalSetting>(from);
        }

        public async Task<List<PersonalSetting>> LoadMany(string[] battletags)
        {
            var settings = CreateCollection<PersonalSetting>();
            var result = await settings
                .Aggregate()
                .Match(p => battletags.Contains(p.Id))
                .ToListAsync();
            return result;
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
    }
}