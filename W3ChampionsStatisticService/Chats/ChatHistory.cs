using System.Collections.Generic;

namespace W3ChampionsStatisticService.Chats
{
    public class ChatHistory : Dictionary<string, List<ChatMessage>>
    {
        public void AddMessage(string chatRoom, ChatMessage message)
        {
            if (!ContainsKey(chatRoom))
            {
                Add(chatRoom, new List<ChatMessage> { message });
            }
            else
            {
                this[chatRoom].Add(message);
                if (this[chatRoom].Count > 50)
                {
                    this[chatRoom].RemoveAt(0);
                }
            }
        }

        public List<ChatMessage> GetMessages(string chatRoom)
        {
            if (!ContainsKey(chatRoom))
            {
                return new List<ChatMessage>();
            }

            return this[chatRoom];
        }
    }
}