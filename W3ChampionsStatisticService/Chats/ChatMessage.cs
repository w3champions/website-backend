using System;

namespace W3ChampionsStatisticService.Chats
{
    public class ChatMessage
    {
        private readonly DateTimeOffset _time;
        public UserDto User { get; }
        public string Message { get; }

        public string Time => _time.ToString();

        public ChatMessage(UserDto user, string message)
        {
            User = user;
            Message = message;
            _time = DateTimeOffset.UtcNow;
        }
    }
}