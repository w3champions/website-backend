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
        var mongoCollection = CreateCollection<NewsMessage>();
        return mongoCollection
            .Find(r => true)
            .SortByDescending(m => m.Id)
            .Limit(limit).ToListAsync();
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
        return Upsert(newsMessage, n => n.Id == newsMessage.Id);
    }
}
