using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.Chats
{
    public class ChatUser
    {
        public ChatUser(string battleTag)
        {
            BattleTag = battleTag;
            Name = battleTag.Split("#")[0];
        }

        [BsonId]
        public string BattleTag { get; set; }
        public string Name { get; set; }
    }
}