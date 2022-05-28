using W3C.Domain.CommonValueObjects;

namespace W3C.Domain.MatchmakingService.MatchmakingContracts
{
    public class MapForceComputer
    {
        public int Slot { get; set; }
        public Color Color { get; set; }
        public Race Race { get; set; }
        public Computer Difficulty { get; set; }
    }
}
