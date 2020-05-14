namespace W3ChampionsStatisticService.PadEvents
{
    internal interface ISyncable
    {
        public bool wasSyncedJustNow { get; }
        public int id { get; }
    }
}