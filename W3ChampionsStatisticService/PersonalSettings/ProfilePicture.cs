using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class ProfilePicture
    {
        public static ProfilePicture Default()
        {
            return new ProfilePicture(AvatarCategory.Total, 0);
        }

        public AvatarCategory Race { get; }
        public long PictureId { get; }

        public ProfilePicture(AvatarCategory race, long pictureId)
        {
            Race = race;
            PictureId = pictureId;
        }
    }
}