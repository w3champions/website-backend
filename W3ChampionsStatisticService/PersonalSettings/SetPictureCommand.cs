using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class SetPictureCommand
    {
        public long PictureId { get; set; }
        public Race Race { get; set; }
    }
}