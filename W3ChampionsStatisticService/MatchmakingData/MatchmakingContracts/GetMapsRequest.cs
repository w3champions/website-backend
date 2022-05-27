namespace W3ChampionsStatisticService.PadEvents.MatchmakingContracts
{
    public class GetMapsRequest
    {
        public string Filter { get; set; }
        public int Offset { get; set; } = 0;
        public int Limit { get; set; } = 10;
    }
}
