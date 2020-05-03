namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerId
    {
        public static PlayerId Create(string id, string nameTag)
        {
            return new PlayerId
            {
                Id = id,
                Name = nameTag.Split("#")[0],
                BattleTag = nameTag.Split("#")[1]
            };
        }

        public string Name { get; set; }
        public string BattleTag { get; set; }
        public string Id { get; set; }
    }
}