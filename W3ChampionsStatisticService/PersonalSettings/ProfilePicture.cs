using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class ProfilePicture
    {
        public static ProfilePicture Default()
        {
            return new ProfilePicture()
            {
                Race = AvatarCategory.Total,
                PictureId = 0
            };
        }

        public AvatarCategory Race { get; set; }
        public long PictureId { get; set; }
        public bool IsClassic { get; set; }
    }
}