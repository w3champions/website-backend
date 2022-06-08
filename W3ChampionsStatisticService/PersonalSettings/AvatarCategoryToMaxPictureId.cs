using W3C.Domain.CommonValueObjects;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class AvatarCategoryToMaxPictureId
    {
        public AvatarCategory AvatarType { get; }
        public long Max { get; }

        public AvatarCategoryToMaxPictureId(AvatarCategory avatarType, long max)
        {
            AvatarType = avatarType;
            Max = max;
        }
    }
}