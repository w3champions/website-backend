namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats
{
    public class HeroStat
    {
        public string Icon { get; set; }
        public int Count { get; set; }

        public static HeroStat Create(string heroIcon)
        {
            return new HeroStat
            {
                Icon = heroIcon,
                Count = 0
            };
        }
    }
}