namespace W3ChampionsStatisticService.Chats
{
    public class ChatMessage
    {
        public UserDto User { get; }
        public string Message { get; }

        public ChatMessage(UserDto user, string message)
        {
            User = user;
            Message = message;
        }
    }
}