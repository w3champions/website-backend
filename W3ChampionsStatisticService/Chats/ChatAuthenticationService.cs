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

        public async Task<UserDto> GetUser(string chatApiKey, string battleTag)
        {
            var user = await LoadFirst<ChatUser>(c => c.ApiKey == chatApiKey);
            var userClan = await LoadFirst<ClanMembership>(c => c.Id == battleTag);
            var userSettings = await LoadFirst<PersonalSetting>(c => c.Id == battleTag);
            if (user != null)
            {
                return new UserDto(user.Name, user.BattleTag, userClan?.ClanId, userSettings?.ProfilePicture, true);
            }
            user = new ChatUser(battleTag);
            return new UserDto(user.Name, user.BattleTag, userClan?.ClanId, userSettings?.ProfilePicture, false);
        }

        public Task SaveUser(ChatUser user)
        {
            return Upsert(user, c => c.BattleTag == user.BattleTag);
        }

        public Task<ChatUser> GetUserByBattleTag(string battleTag)
        {
            return LoadFirst<ChatUser>(c => c.BattleTag == battleTag);
        }
    }
}