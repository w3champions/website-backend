using System;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.Chats
{
    public class ChatUser
    {
        public ChatUser(string battleTag)
        {
            BattleTag = battleTag;
            Name = battleTag.Split("#")[0];
            ApiKey = NewApiKey();
        }

        [BsonId]
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public string ApiKey { get; set; }

        public void CreatApiKey()
        {
            ApiKey = NewApiKey();
        }

        private static string NewApiKey()
        {
            return Guid.NewGuid().ToString();
        }
    }
}