namespace W3ChampionsStatisticService.Chats
{
    public class UserDto
    {
        public string Name { get; }
        public string BattleTag { get; }
        public bool VerifiedBattletag { get; }

        public UserDto(string name, string battleTag, bool verifiedBattletag)
        {
            Name = name;
            BattleTag = battleTag;
            VerifiedBattletag = verifiedBattletag;
        }
    }
}