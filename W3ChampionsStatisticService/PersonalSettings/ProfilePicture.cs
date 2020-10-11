using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class ProfilePicture
    {
        public static ProfilePicture Default()
        {
            return new ProfilePicture(AvatarCategory.Total, 0, null);
        }

        public AvatarCategory Race { get; }
        public int PictureId { get; }
        public string Description { get; }

        public ProfilePicture(AvatarCategory race, int pictureId, string description)
        {
            Race = race;
            PictureId = pictureId;
            Description = description;
        }
    }
}