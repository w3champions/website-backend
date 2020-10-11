using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class SetPictureCommand
    {
        public int pictureId { get; set; }
        public AvatarCategory avatarCategory { get; set; }
        public string description { get; set; }
    }
}