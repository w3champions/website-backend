using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public interface IPersonalSettingsRepository
    {
        Task<PersonalSetting> Load(string battletag);
        Task Save(PersonalSetting setting);
    }

    public class PersonalSettingsRepository : MongoDbRepositoryBase, IPersonalSettingsRepository
    {
        public PersonalSettingsRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public async Task<PersonalSetting> Load(string battletag)
        {
            var ranks = CreateCollection<PersonalSetting>();
            var players = CreateCollection<PlayerProfile>();
            var result = await ranks
                .Aggregate()
                .Match(p => p.Id == battletag)
                .Lookup<PersonalSetting, PlayerProfile, PersonalSetting>(players,
                    rank => rank.Id,
                    player => player.Id,
                    rank => rank.Players)
                .FirstOrDefaultAsync();
            return result;
        }

        public Task Save(PersonalSetting setting)
        {
            return Upsert(setting, p => p.Id == setting.Id);
        }
    }
}