using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace W3ChampionsStatisticService.Cache
{
    public class InMemoryCacheData<T>: ICacheData<T>
    {
        // NOTE: It is intentional to have different semaphores for different generic types
        // ReSharper disable once StaticMemberInGenericType
        private static readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

        private readonly CacheDataOptions<T> _cacheDataOptions;
        private readonly IMemoryCache _memoryCache;

        public InMemoryCacheData(IOptions<CacheDataOptions<T>> cacheDataOptions, IMemoryCache memoryCache)
        {
            _cacheDataOptions = cacheDataOptions.Value;
            _memoryCache = memoryCache;
        }

        public Task<T> GetCachedOrRequestAsync(Func<Task<T>> requestDataCallbackAsync, string key = null)
        {
            return _memoryCache.GetOrCreateAsync(typeof(T).FullName + key, async cacheEntry =>
            {
                if (_cacheDataOptions.CacheDuration.HasValue)
                {
                    cacheEntry.SetSlidingExpiration(_cacheDataOptions.CacheDuration.Value);
                }

                if (!_cacheDataOptions.LockDuringFetch)
                {
                    return await requestDataCallbackAsync();
                }

                await _semaphoreSlim.WaitAsync();
                try
                {
                    return await requestDataCallbackAsync();
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            });
        }
    }
}