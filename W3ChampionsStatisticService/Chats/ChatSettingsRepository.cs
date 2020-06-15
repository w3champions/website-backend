using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Chats
{
    public class ChatSettingsRepository : MongoDbRepositoryBase, IChatSettingsRepository
    {
        public ChatSettingsRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public Task Save(ChatSettings chatSettings)
        {
            return Upsert(chatSettings);
        }

        public Task<ChatSettings> Load(string id)
        {
            return LoadFirst<ChatSettings>(id);
        }
    }
}