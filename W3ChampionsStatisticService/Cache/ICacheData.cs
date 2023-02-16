using System;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Cache
{
    public interface ICacheData<T>
    {
        Task<T> GetCachedOrRequestAsync(Func<Task<T>> requestDataCallbackAsync, string key);
    }
}