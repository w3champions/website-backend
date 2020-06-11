namespace W3ChampionsStatisticService.Chats
{
    public class UserDto
    {
        public string Name { get; }
        public string BattleTag { get; }
        public bool UnverifiedBattletag { get; }

        public UserDto(string name, string battleTag, bool unverifiedBattletag)
        {
            Name = name;
            BattleTag = battleTag;
            UnverifiedBattletag = unverifiedBattletag;
        }
    }
}