namespace W3C.Domain.MatchmakingService;

internal interface ISyncable
{
    public bool wasSyncedJustNow { get; }
    public int id { get; }
}
