namespace W3ChampionsStatisticService.W3ChampionsStats.MmrDistribution
{
    public class MmrCount
    {
        public int Mmr { get; set; }
        public int Count { get; set; }

        public MmrCount(int mmr, int count)
        {
            Mmr = mmr;
            Count = count;
        }
    }
}