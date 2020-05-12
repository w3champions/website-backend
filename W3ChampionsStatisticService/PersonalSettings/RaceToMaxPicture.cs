using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class RaceToMaxPicture
    {
        public Race Race { get; }
        public long Max { get; }

        public RaceToMaxPicture(Race race, long max)
        {
            Race = race;
            Max = max;
        }
    }
}