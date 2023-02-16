using System;

namespace W3ChampionsStatisticService.Cache
{
    public class CacheDataOptions<T>
    {
        public bool LockDuringFetch { get; set; } = true;
        public TimeSpan? CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
    }
}