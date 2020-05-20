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
            var players = CreateCollection<PlayerProfileVnext>();
            var result = await settings
                .Aggregate()
                .Match(p => p.Id == battletag)
                .Lookup<PersonalSetting, PlayerProfileVnext, PersonalSetting>(players,
                    rank => rank.Id,
                    player => player.BattleTag,
                    rank => rank.Players)
                .FirstOrDefaultAsync();
            return result;
        }

        public Task Save(PersonalSetting setting)
        {
            setting.Players = null;
            return Upsert(setting, p => p.Id == setting.Id);
        }
    }
}