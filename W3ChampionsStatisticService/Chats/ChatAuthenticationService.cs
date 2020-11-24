using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.Clans;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Chats
{
    public class ChatAuthenticationService : MongoDbRepositoryBase
    {
        public ChatAuthenticationService(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public async Task<UserDto> GetUser(string battleTag)
        {
            var userClan = await LoadFirst<ClanMembership>(c => c.Id == battleTag);
            var userSettings = await LoadFirst<PersonalSetting>(c => c.Id == battleTag);
            return new UserDto(
                battleTag.Split("#")[0],
                battleTag,
                userClan?.ClanId,
                userSettings);
        }
    }
}