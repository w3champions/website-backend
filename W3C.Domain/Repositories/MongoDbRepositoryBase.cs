using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

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

    private IMongoDatabase CreateClient()
    {
        // Do not expose the client to derived classes to enforce the usage of this classes functions which are session aware!
        var database = _mongoClient.GetDatabase(_databaseName);
        return database;
    }

    protected Task<T> LoadFirst<T>(string id) where T : IIdentifiable
    {
        return Find(Builders<T>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync();
    }

    protected IFindFluent<T, T> Find<T>(
        FilterDefinition<T> filter, 
        SortDefinition<T> sortBy = null, 
        int? limit = null,
        int? offset = null)
    {
        var collection = CreateCollection<T>();
        var session = _transactionCoordinator?.GetCurrentSession();
        var findFluent = session != null
            ? collection.Find(session, filter)
            : collection.Find(filter);
        
        if (sortBy != null)
        {
            findFluent = findFluent.Sort(sortBy);
        }
        
        findFluent = findFluent.Skip(offset);
        findFluent = findFluent.Limit(limit);
        
        return findFluent;
    }


    protected IMongoQueryable<TDocument> AsQueryable<TDocument>(AggregateOptions aggregateOptions = null)
    {

        var mongoCollection = CreateCollection<TDocument>();
        var session = _transactionCoordinator?.GetCurrentSession();
        return session != null
            ? mongoCollection.AsQueryable(session, aggregateOptions)
            : mongoCollection.AsQueryable(aggregateOptions);
    }

    protected Task Insert<T>(T element)
    {
        var mongoCollection = CreateCollection<T>();
        var session = _transactionCoordinator?.GetCurrentSession();
        return session != null
            ? mongoCollection.InsertOneAsync(session, element)
            : mongoCollection.InsertOneAsync(element);
    }

    protected Task<UpdateResult> UpdateOneAsync<T>(FilterDefinition<T> filter, UpdateDefinition<T> update, UpdateOptions options = null)
    {
        var mongoCollection = CreateCollection<T>();
        var session = _transactionCoordinator?.GetCurrentSession();
        return session != null
            ? mongoCollection.UpdateOneAsync(session, filter, update, options)
            : mongoCollection.UpdateOneAsync(filter, update, options);
    }

    protected Task<UpdateResult> UpdateManyAsync<T>(FilterDefinition<T> filter, UpdateDefinition<T> update, UpdateOptions options = null)
    {
        var mongoCollection = CreateCollection<T>();
        var session = _transactionCoordinator?.GetCurrentSession();
        return session != null
            ? mongoCollection.UpdateManyAsync(session, filter, update, options)
            : mongoCollection.UpdateManyAsync(filter, update, options);
    }

    protected Task<TProjection> FindOneAndUpdateAsync<T, TProjection>(FilterDefinition<T> filter, UpdateDefinition<T> update, FindOneAndUpdateOptions<T, TProjection> options = null, CancellationToken cancellationToken = default)
    {
        var mongoCollection = CreateCollection<T>();
        var session = _transactionCoordinator?.GetCurrentSession();
        return session != null
            ? mongoCollection.FindOneAndUpdateAsync(session, filter, update, options, cancellationToken)
            : mongoCollection.FindOneAndUpdateAsync(filter, update, options, cancellationToken);
    }

    protected IFindFluent<T, T> LoadSince<T>(DateTimeOffset since) where T : IVersionable
    {
        return Find(Builders<T>.Filter.Gt(m => m.LastUpdated, since));
    }

    private IMongoCollection<T> CreateCollection<T>(string collectionName = null)
    {
        // Do not expose the collection to derived classes to enforce the usage of this classes functions which are session aware!
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

    protected Task<BulkWriteResult<T>> BulkWriteAsync<T>(IEnumerable<WriteModel<T>> requests, BulkWriteOptions options = null)
    {
        var collection = CreateCollection<T>();
        var session = _transactionCoordinator?.GetCurrentSession();
        return session != null
            ? collection.BulkWriteAsync(session, requests, options)
            : collection.BulkWriteAsync(requests, options);
    }

    protected Task<string> CreateIndexOneAsync<T>(CreateIndexModel<T> model, CreateOneIndexOptions options = null, CancellationToken cancellationToken = default)
    {
        var collection = CreateCollection<T>();
        return collection.Indexes.CreateOneAsync(model, options, cancellationToken);
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
