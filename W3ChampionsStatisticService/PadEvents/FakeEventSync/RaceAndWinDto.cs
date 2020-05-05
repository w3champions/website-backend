using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PadEvents.FakeEventSync
{
    public class RaceAndWinDto
    {
        public RaceAndWinDto(Race race, long count)
        {
            Race = race;
            Count = count;
        }

        public Race Race { get; set;  }
        public long Count { get; set; }
    }
}