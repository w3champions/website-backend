namespace W3ChampionsStatisticService.PersonalSettings;

public class SpecialPicture(int pictureId, string description)
{
    public int PictureId { get; set; } = pictureId;
    public string Description { get; set; } = description;
}
