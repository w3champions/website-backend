using System;

namespace W3ChampionsStatisticService.Cache;

public class CacheOptionsFor<T> where T : class
{
    public bool LockDuringFetch { get; set; } = true;
    public TimeSpan? CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}
