using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Chats
{
    public class ChatAuthenticationService : MongoDbRepositoryBase
    {
        public ChatAuthenticationService(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public Task<ChatUser> GetUser(string chatApiKey)
        {
            return LoadFirst<ChatUser>(c => c.ApiKey == chatApiKey);
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