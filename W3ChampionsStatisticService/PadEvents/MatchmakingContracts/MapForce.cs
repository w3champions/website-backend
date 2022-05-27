namespace W3ChampionsStatisticService.PadEvents.MatchmakingContracts
{
    public class MapForce
    {
        public int Team { get; set; }
        public int[] Slots { get; set; }
        public MapForceComputer[] Computers { get; set; } = new MapForceComputer[0];
    }
}
