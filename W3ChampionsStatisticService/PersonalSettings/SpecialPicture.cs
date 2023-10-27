namespace W3ChampionsStatisticService.PersonalSettings;

public class SpecialPicture
{
    public SpecialPicture(int pictureId, string description)
    {
        PictureId = pictureId;
        Description = description;
    }

    public int PictureId { get; set; }
    public string Description { get; set; }
}
