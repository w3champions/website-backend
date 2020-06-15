using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Chats
{
    public class ChatSettings : IIdentifiable
    {
        public ChatSettings(string battleTag)
        {
            BattleTag = battleTag;
        }

        public string Id => BattleTag;

        public string BattleTag { get; set; }
        public string DefaultChat { get; set; }
        public bool? HideChat { get; set; }

        public void Update(string defaultChat, bool? hideChat)
        {
            if (hideChat != null) HideChat = hideChat;
            if (defaultChat != null) DefaultChat = defaultChat;
        }
    }
}