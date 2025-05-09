using MongoDB.Bson;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Admin;

public class NewsRepository : MongoDbRepositoryBase, INewsRepository
{
    public NewsRepository(MongoClient mongoClient) : base(mongoClient)
    {
    }

    public Task<List<NewsMessage>> Get(int? limit = 5)
    {
        return LoadAll(
            sortBy: Builders<NewsMessage>.Sort.Descending(m => m.Id),
            limit: limit
        );
    }

    public Task Save(NewsMessage newsMessage)
    {
        return Insert(newsMessage);
    }

    public Task DeleteNews(ObjectId objectId)
    {
        return Delete<NewsMessage>(n => n.Id == objectId);
    }

    public Task UpsertNews(NewsMessage newsMessage)
    {
        return Upsert(newsMessage, Builders<NewsMessage>.Filter.Eq(n => n.Id, newsMessage.Id));
    }
}
