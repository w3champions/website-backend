namespace W3C.Domain.MatchmakingService.MatchmakingContracts
{
    public class SeasonMapContract
    {
        public int Id { get; set; }
        public string GameMode { get; set; }
        public MapContract[] Maps { get; set; }
    }
}
