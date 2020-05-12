using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PadEvents.FakeEventSync
{
    public class RaceAndWinDto
    {
        public RaceAndWinDto(Race race, long count, bool won)
        {
            Race = race;
            Count = count;
            Won = won;
        }

        public Race Race { get; set;  }
        public long Count { get; set; }
        public bool Won { get; set; }
    }
}