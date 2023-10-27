using W3C.Domain.CommonValueObjects;

namespace W3ChampionsStatisticService.PersonalSettings;

public class SetPictureCommand
{
    public int pictureId { get; set; }
    public AvatarCategory avatarCategory { get; set; }
    public string description { get; set; }
    public bool isClassic { get; set; }
}
