using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3ChampionsStatisticService.Admin;

namespace W3ChampionsStatisticService.Ports
{
    public interface ILoadingScreenTipsRepository
    {
        Task<List<LoadingScreenTip>> Get(int? limit = 5);
        Task<LoadingScreenTip> GetRandomTip();
        Task Save(LoadingScreenTip loadingScreenTip);
        Task DeleteTip(ObjectId objectId);
        Task UpsertTip(LoadingScreenTip loadingScreenTip);

    }
}
