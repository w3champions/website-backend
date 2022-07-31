using System;
using W3C.Domain.CommonValueObjects;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class ProfilePicture
    {
        public static ProfilePicture Default()
        {
            var random = new Random();
            return new ProfilePicture()
            {
                Race = AvatarCategory.Starter,
                PictureId = random.Next(1,5),
            };
        }

        public AvatarCategory Race { get; set; }
        public long PictureId { get; set; }
        public bool IsClassic { get; set; }
    }
}