using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PadEvents.MatchmakingContracts
{
    public class MapForceComputer
    {
        public int Slot { get; set; }
        public Color Color { get; set; }
        public Race Race { get; set; }
        public Computer Difficulty { get; set; }
    }
}
