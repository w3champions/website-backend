using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace W3ChampionsStatisticService.Cache;

public class InMemoryCachedDataProvider<T>(IOptions<CacheOptionsFor<T>> cacheDataOptions, IMemoryCache memoryCache) : ICachedDataProvider<T> where T : class
{
    // NOTE: It is intentional to have different semaphores for different generic types
    // ReSharper disable once StaticMemberInGenericType
    private static readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private readonly CacheOptionsFor<T> _cacheOptions = cacheDataOptions.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;

    public async Task<T> GetCachedOrRequestAsync(
        Func<Task<T>> requestDataCallbackAsync,
        string key = null,
        TimeSpan? customExpiration = null)
    {
        if (_cacheOptions.LockDuringFetch)
        {
            await _semaphoreSlim.WaitAsync();
        }

        try
        {
            return await _memoryCache.GetOrCreateAsync(typeof(T).FullName + key, async cacheEntry =>
            {
                var expiration = customExpiration ?? _cacheOptions.CacheDuration;

                if (expiration.HasValue)
                {
                    cacheEntry.SetAbsoluteExpiration(expiration.Value);
                }

                return await requestDataCallbackAsync();
            });
        }
        finally
        {
            if (_cacheOptions.LockDuringFetch)
            {
                _semaphoreSlim.Release();
            }
        }
    }
}
