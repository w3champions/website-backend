using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace W3C.Domain.Repositories;

public class MongoDbRepositoryBase
{
    private readonly MongoClient _mongoClient;
    private readonly string _databaseName = "W3Champions-Statistic-Service";
    private readonly ITransactionCoordinator _transactionCoordinator;

    public MongoDbRepositoryBase(MongoClient mongoClient, ITransactionCoordinator transactionCoordinator = null)
    {
        _mongoClient = mongoClient ?? throw new ArgumentNullException(nameof(mongoClient));
        _transactionCoordinator = transactionCoordinator;
    }

    protected IMongoDatabase CreateClient()
    {
        var database = _mongoClient.GetDatabase(_databaseName);
        return database;
    }

    protected Task<T> LoadFirst<T>(Expression<Func<T, bool>> expression)
    {
        var mongoCollection = CreateCollection<T>();
        var session = _transactionCoordinator?.GetCurrentSession();
        return session != null
            ? mongoCollection.Find(session, expression).FirstOrDefaultAsync()
            : mongoCollection.Find(expression).FirstOrDefaultAsync();
    }

    protected Task<T> LoadFirst<T>(string id) where T : IIdentifiable
    {
        return LoadFirst<T>(x => x.Id == id);
    }

    protected Task Insert<T>(T element)
    {
        var mongoCollection = CreateCollection<T>();
        var session = _transactionCoordinator?.GetCurrentSession();
        return session != null
            ? mongoCollection.InsertOneAsync(session, element)
            : mongoCollection.InsertOneAsync(element);
    }

    protected async Task<List<T>> LoadAll<T>(Expression<Func<T, bool>> expression = null, int? limit = null)
    {
        expression ??= l => true;
        var mongoCollection = CreateCollection<T>();
        var session = _transactionCoordinator?.GetCurrentSession();

        var findFluent = session != null
            ? mongoCollection.Find(session, expression)
            : mongoCollection.Find(expression);

        if (limit.HasValue)
        {
            findFluent = findFluent.Limit(limit);
        }

        return await findFluent.ToListAsync();
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

    protected async Task Upsert<T>(T insertObject, Expression<Func<T, bool>> identityQuerry)
    {
        var mongoCollection = CreateCollection<T>();
        var session = _transactionCoordinator?.GetCurrentSession();
        var options = new FindOneAndReplaceOptions<T> { IsUpsert = true };

        if (session != null)
        {
            await mongoCollection.FindOneAndReplaceAsync(session, identityQuerry, insertObject, options);
        }
        else
        {
            await mongoCollection.FindOneAndReplaceAsync(identityQuerry, insertObject, options);
        }
    }

    protected Task UpsertTimed<T>(T insertObject, Expression<Func<T, bool>> identityQuerry) where T : IVersionable
    {
        insertObject.LastUpdated = DateTimeOffset.UtcNow;
        return Upsert(insertObject, identityQuerry);
    }

    protected Task Upsert<T>(T insertObject) where T : IIdentifiable
    {
        return Upsert(insertObject, x => x.Id == insertObject.Id);
    }

    protected Task UpsertMany<T>(List<T> insertObjects) where T : IIdentifiable
    {
        if (!insertObjects.Any()) return Task.CompletedTask;

        var collection = CreateCollection<T>();
        var session = _transactionCoordinator?.GetCurrentSession();
        var bulkOps = insertObjects
            .Select(record => new ReplaceOneModel<T>(Builders<T>.Filter
            .Where(x => x.Id == record.Id), record)
            { IsUpsert = true })
            .Cast<WriteModel<T>>().ToList();

        return session != null
            ? collection.BulkWriteAsync(session, bulkOps)
            : collection.BulkWriteAsync(bulkOps);
    }

    protected async Task Delete<T>(Expression<Func<T, bool>> deleteQuery)
    {
        var mongoCollection = CreateCollection<T>();
        var session = _transactionCoordinator?.GetCurrentSession();

        if (session != null)
        {
            await mongoCollection.DeleteOneAsync(session, deleteQuery);
        }
        else
        {
            await mongoCollection.DeleteOneAsync(deleteQuery);
        }
    }

    protected Task Delete<T>(string id) where T : IIdentifiable
    {
        return Delete<T>(x => x.Id == id);
    }

    protected async Task UnsetOne<T>(FieldDefinition<T> fieldName, string id) where T : IIdentifiable
    {
        var mongoCollection = CreateCollection<T>();
        var session = _transactionCoordinator?.GetCurrentSession();
        var updateDefinition = Builders<T>.Update.Unset(fieldName);
        var filter = Builders<T>.Filter.Eq(x => x.Id, id);

        if (session != null)
        {
            await mongoCollection.UpdateOneAsync(session, filter, updateDefinition);
        }
        else
        {
            await mongoCollection.UpdateOneAsync(filter, updateDefinition);
        }
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
