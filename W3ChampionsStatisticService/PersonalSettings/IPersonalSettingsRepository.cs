using System.Threading.Tasks;
using MongoDB.Driver;
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

        public Task<PersonalSetting> Load(string battletag)
        {
            return LoadFirst<PersonalSetting>(p => p.Id == battletag);
        }

        public Task Save(PersonalSetting setting)
        {
            return Upsert(setting, p => p.Id == setting.Id);
        }
    }
}