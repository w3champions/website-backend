namespace W3ChampionsStatisticService.PersonalSettings;

public class WinsToPictureId(int pictureId, int neededWins)
{
    public int PictureId { get; } = pictureId;
    public int NeededWins { get; } = neededWins;
}
