using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace W3ChampionsStatisticService.Cache
{
    public class InMemoryCacheData<T> : ICacheData<T>
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

        public async Task<T> GetCachedOrRequestAsync(Func<Task<T>> requestDataCallbackAsync, string key = null)
        {
            if (_cacheDataOptions.LockDuringFetch)
            {
                await _semaphoreSlim.WaitAsync();
            }

            try
            {
                return await _memoryCache.GetOrCreateAsync(typeof(T).FullName + key, async cacheEntry =>
                {
                    if (_cacheDataOptions.CacheDuration.HasValue)
                    {
                        cacheEntry.SetAbsoluteExpiration(_cacheDataOptions.CacheDuration.Value);
                    }

                    return await requestDataCallbackAsync();
                });
            }
            finally
            {
                if (_cacheDataOptions.LockDuringFetch)
                {
                    _semaphoreSlim.Release();
                }
            }
        }
    }
}