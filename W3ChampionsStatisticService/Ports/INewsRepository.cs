using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3ChampionsStatisticService.Admin;

namespace W3ChampionsStatisticService.Ports
{
    public interface INewsRepository
    {
        Task<List<NewsMessage>> Get(int? limit = 5);
        Task Save(NewsMessage newsMessage);
        Task DeleteNews(ObjectId objectId);
        Task UpsertNews(NewsMessage newsMessage);
    }
}