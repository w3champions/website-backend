using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.CommonValueObjects;
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
            var players = CreateCollection<PlayerRaceWins>();
            var result = await settings
                .Aggregate()
                .Match(p => p.Id == battletag)
                .Lookup<PersonalSetting, PlayerRaceWins, PersonalSetting>(players,
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

        public Task<PlayerRaceWins> LoadPlayerRaceWins(string battleTag)
        {
            return LoadFirst<PlayerRaceWins>(p => p.Id == battleTag);
        }

        public Task UpsertPlayerRaceWin(PlayerRaceWins player)
        {
            return Upsert(player, p => p.Id == player.Id);
        }
    }
}