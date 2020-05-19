namespace W3ChampionsStatisticService.Chats
{
    public class ChatUser
    {
        public ChatUser(string battleTag, string name)
        {
            BattleTag = battleTag;
            Name = name;
        }

        public string BattleTag { get; set; }
        public string Name { get; set; }
    }
}