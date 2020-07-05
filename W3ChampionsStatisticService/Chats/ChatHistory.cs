using System.Collections.Generic;

namespace W3ChampionsStatisticService.Chats
{
    public class ChatHistory : Dictionary<string, List<ChatMessage>>
    {
        public void AddMessage(string chatRoom, UserDto userName, string message)
        {
            if (!ContainsKey(chatRoom))
            {
                Add(chatRoom, new List<ChatMessage> { new ChatMessage(userName, message) });
            }
            else
            {
                this[chatRoom].Add(new ChatMessage(userName, message));
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