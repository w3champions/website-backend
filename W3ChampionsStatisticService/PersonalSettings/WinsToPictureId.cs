namespace W3ChampionsStatisticService.PersonalSettings
{
    public class WinsToPictureId
    {
        public int PictureId { get; }
        public int NeededWins { get; }

        public WinsToPictureId(int pictureId, int neededWins)
        {
            PictureId = pictureId;
            NeededWins = neededWins;
        }
    }
}