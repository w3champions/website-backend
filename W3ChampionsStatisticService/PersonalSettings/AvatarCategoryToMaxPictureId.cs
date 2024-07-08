using W3C.Domain.CommonValueObjects;

namespace W3ChampionsStatisticService.PersonalSettings;

public class AvatarCategoryToMaxPictureId(AvatarCategory avatarType, long max)
{
    public AvatarCategory AvatarType { get; } = avatarType;
    public long Max { get; } = max;
}
