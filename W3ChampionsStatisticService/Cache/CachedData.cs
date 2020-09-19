using System;

namespace W3ChampionsStatisticService.Cache
{
    public class CachedData<T>
    {
        private readonly Func<T> _fetchFunc;
        private readonly TimeSpan _cacheDuration;
        private readonly Func<T, bool> _cacheCondition;

        private readonly object _changingLock = new object();
        private readonly object _regularLock = new object();

        private T _cachedData;
        private DateTime _cachedUntil;

        public CachedData(Func<T> fetchFunc, TimeSpan cacheDuration, Func<T, bool> cacheCondition = null)
        {
            _fetchFunc = fetchFunc;
            _cacheDuration = cacheDuration;
            _cacheCondition = cacheCondition;
        }

        public bool CacheExpired()
        {
            return DateTime.Now > this._cachedUntil;
        }

        public void InvalidateCache()
        {
            _cachedUntil = DateTime.Now.AddDays(-1);
        }

        public T GetCachedData()
        {
            lock (_regularLock)
            {
                if (CacheExpired())
                {
                    lock (_changingLock)
                    {
                        _cachedData = _fetchFunc();

                        if (_cacheCondition == null || _cacheCondition(_cachedData))
                        {
                            _cachedUntil = DateTime.Now.Add(_cacheDuration);
                        }
                    }
                }

                return _cachedData;
            }
        }
    }
}
