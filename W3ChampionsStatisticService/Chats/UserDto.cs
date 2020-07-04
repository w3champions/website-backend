using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.Chats
{
    public class UserDto
    {
        public string Name { get; }
        public string BattleTag { get; }
        public string ClanTag { get; }
        public ProfilePicture ProfilePicture { get; }
        public bool VerifiedBattletag { get; }

        public UserDto(
            string name,
            string battleTag,
            string clanTag,
            ProfilePicture profilePicture,
            bool verifiedBattletag)
        {
            Name = name;
            BattleTag = battleTag;
            VerifiedBattletag = verifiedBattletag;
            ClanTag = clanTag;
            ProfilePicture = profilePicture;
        }
    }
}