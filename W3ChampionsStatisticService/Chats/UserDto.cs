using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.Chats
{
    public class UserDto
    {
        public string Name { get; }
        public string BattleTag { get; }
        public string ClanTag { get; }
        public ProfilePicture ProfilePicture { get; }

        public string Alias { get; set; }
        public string Color { get; set; }

        public UserDto(
            string name,
            string battleTag,
            string clanTag,
            PersonalSetting personalSettings)
        {
            Name = name;
            BattleTag = battleTag;
            ClanTag = clanTag;
            ProfilePicture = personalSettings?.ProfilePicture ?? ProfilePicture.Default();

            if (personalSettings != null)
            {
                Alias = personalSettings.ChatAlias;
                Color = personalSettings.ChatColor;
            }
        }
    }
}