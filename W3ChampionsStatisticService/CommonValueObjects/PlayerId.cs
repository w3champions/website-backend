namespace W3ChampionsStatisticService.CommonValueObjects
{
    public class PlayerId
    {
        public static PlayerId Create(string nameTag)
        {
            return new PlayerId
            {
                Name = nameTag.Split("#")[0],
                BattleTag = nameTag
            };
        }

        public string Name { get; set; }
        public string BattleTag { get; set; }
    }
}