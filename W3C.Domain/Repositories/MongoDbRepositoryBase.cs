using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace W3C.Domain.Repositories;

public class MongoDbRepositoryBase
{
    private readonly MongoClient _mongoClient;
    private readonly string _databaseName = "W3Champions-Statistic-Service";

    public MongoDbRepositoryBase(MongoClient mongoClient)
    {
        _mongoClient = mongoClient;
    }

    protected IMongoDatabase CreateClient()
    {
        var database = _mongoClient.GetDatabase(_databaseName);
        return database;
    }

    protected Task<T> LoadFirst<T>(Expression<Func<T, bool>> expression, int? season = 1)
    {
        IMongoCollection<T> mongoCollection;
        if (typeof(ISeasonal).IsAssignableFrom(typeof(T)))
        {
            mongoCollection = CreateSeasonalCollection<T>(season ?? 1);
        }
        else
        {
            mongoCollection = CreateCollection<T>();
        }
        return mongoCollection.FindSync(expression).FirstOrDefaultAsync();
    }

    protected Task<T> LoadFirst<T>(string id) where T : IIdentifiable
    {
        return LoadFirst<T>(x => x.Id == id);
    }

    protected Task Insert<T>(T element)
    {
        var mongoCollection = CreateCollection<T>();
        return mongoCollection.InsertOneAsync(element);
    }

    protected async Task<List<T>> LoadAll<T>(Expression<Func<T, bool>> expression = null, int? limit = null)
    {
        if (expression == null) expression = l => true;
        var mongoCollection = CreateCollection<T>();
        var elements = await mongoCollection.Find(expression).Limit(limit).ToListAsync();
        return elements;
    }

    protected Task<List<T>> LoadSince<T>(DateTimeOffset since) where T : IVersionable
    {
        return LoadAll<T>(m => m.LastUpdated > since);
    }

    protected IMongoCollection<T> CreateCollection<T>(string collectionName = null)
    {
        var mongoDatabase = CreateClient();
        var mongoCollection = mongoDatabase.GetCollection<T>((collectionName ?? typeof(T).Name));
        return mongoCollection;
    }

    protected IMongoCollection<T> CreateSeasonalCollection<T>(int season, string collectionName = null)
    {
        var mongoDatabase = CreateClient();
        var mongoCollection = mongoDatabase.GetCollection<T>(String.Format(collectionName ?? "{0}_{1}", typeof(T).Name, season));
        return mongoCollection;
    }

    protected async Task Upsert<T>(T insertObject, Expression<Func<T, bool>> identityQuery)
    {
        var mongoDatabase = CreateClient();
        IMongoCollection<T> mongoCollection;
        if (insertObject is ISeasonal seasonalObj)
        {
            mongoCollection = mongoDatabase.GetCollection<T>(String.Format("{0}_{1}", typeof(T).Name, seasonalObj.Season));
        } 
        else
        {
            mongoCollection = mongoDatabase.GetCollection<T>(typeof(T).Name);
        }
        await mongoCollection.FindOneAndReplaceAsync(
            identityQuery,
            insertObject,
            new FindOneAndReplaceOptions<T> {IsUpsert = true});
    }

    protected Task UpsertTimed<T>(T insertObject, Expression<Func<T, bool>> identityQuerry) where T : IVersionable
    {
        insertObject.LastUpdated = DateTimeOffset.UtcNow;
        return Upsert(insertObject, identityQuerry);
    }

    protected Task Upsert<T>(T insertObject)  where T : IIdentifiable
    {
        return Upsert(insertObject, x => x.Id == insertObject.Id);
    }

    protected Task UpsertMany<T>(List<T> insertObject) where T : IIdentifiable
    {
        if (!insertObject.Any()) return Task.CompletedTask;

        var collection = CreateCollection<T>();
        var bulkOps = insertObject
            .Select(record => new ReplaceOneModel<T>(Builders<T>.Filter
            .Where(x => x.Id == record.Id), record) {IsUpsert = true})
            .Cast<WriteModel<T>>().ToList();
        return collection.BulkWriteAsync(bulkOps);
    }

    protected async Task Delete<T>(Expression<Func<T, bool>> deleteQuery)
    {
        var mongoDatabase = CreateClient();
        var mongoCollection = mongoDatabase.GetCollection<T>(typeof(T).Name);
        await mongoCollection.DeleteOneAsync<T>(deleteQuery);
    }

    protected Task Delete<T>(string id) where T : IIdentifiable
    {
        return Delete<T>(x => x.Id == id);
    }

    protected async Task UnsetOne<T>(FieldDefinition<T> fieldName, string id) where T : IIdentifiable
    {
        // $unset
        var mongoDatabase = CreateClient();
        var mongoCollection = mongoDatabase.GetCollection<T>(typeof(T).Name);
        var updateDefinition = Builders<T>.Update.Unset(fieldName);
        await mongoCollection.UpdateOneAsync(x => x.Id == id, updateDefinition);
    }
}

public interface IIdentifiable
{
    public string Id { get; }
}

public interface IVersionable
{
    public DateTimeOffset LastUpdated { get; set; }
}
