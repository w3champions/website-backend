namespace W3ChampionsStatisticService.PersonalSettings
{
    public class PersonalSetting
    {
        public PersonalSetting(string battleTag)
        {
            Id = battleTag;
        }

        public string ProfileMessage { get; set; }
        public string ProfilePicture { get; set; }
        public string Id { get; set; }
    }
}