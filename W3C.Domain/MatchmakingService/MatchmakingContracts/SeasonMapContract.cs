namespace W3C.Domain.MatchmakingService.MatchmakingContracts
{
    public class SeasonMapContract
    {
        public int Total { get; set; }
        public string GameMode { get; set; }
        public MapContract[] Maps { get; set; }
    }
}
