namespace W3C.Contracts.Matchmaking
{
    public class SeasonMapContract
    {
        public int Id { get; set; }
        public string GameMode { get; set; }
        public string Type { get; set; }
        public MapContract[] Maps { get; set; }
    }
}
