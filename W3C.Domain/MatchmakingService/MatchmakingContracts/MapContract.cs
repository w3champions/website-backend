namespace W3C.Domain.MatchmakingService.MatchmakingContracts
{
    public class MapContract
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public int MaxTeams { get; set; }
        public MapForce[] MappedForces { get; set; } = new MapForce[0];
        public ExtendedGameMap GameMap { get; set; }
    }
}
