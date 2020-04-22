using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class ProfilePicture
    {
        public static ProfilePicture Default()
        {
            return new ProfilePicture(Race.Total, 0);
        }

        public Race Race { get; }
        public long PictureId { get; }

        public ProfilePicture(Race race, long pictureId)
        {
            Race = race;
            PictureId = pictureId;
        }
    }
}