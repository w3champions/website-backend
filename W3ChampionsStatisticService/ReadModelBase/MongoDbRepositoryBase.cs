using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace W3ChampionsStatisticService.ReadModelBase
{
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

        protected async Task<T> LoadFirst<T>(Expression<Func<T, bool>> expression)
        {
            var mongoCollection = CreateCollection<T>();
            var elements = await mongoCollection.FindAsync(expression);
            return elements.FirstOrDefault();
        }

        protected async Task<List<T>> Load<T>(Expression<Func<T, bool>> expression)
        {
            var mongoCollection = CreateCollection<T>();
            var elements = await mongoCollection.FindAsync(expression);
            return elements.ToList();
        }

        protected async Task<List<T>> LoadAll<T>(Expression<Func<T, bool>> expression = null)
        {
            if (expression == null) expression = l => true;
            var mongoCollection = CreateCollection<T>();
            var elements = await mongoCollection.Find(expression).ToListAsync();
            return elements;
        }

        protected IMongoCollection<T> CreateCollection<T>()
        {
            var mongoDatabase = CreateClient();
            var mongoCollection = mongoDatabase.GetCollection<T>(typeof(T).Name);
            return mongoCollection;
        }

        protected async Task Upsert<T>(T insertObject, Expression<Func<T, bool>> identityQuerry)
        {
            var mongoDatabase = CreateClient();
            var mongoCollection = mongoDatabase.GetCollection<T>(typeof(T).Name);
            await mongoCollection.FindOneAndReplaceAsync(
                identityQuerry,
                insertObject,
                new FindOneAndReplaceOptions<T> {IsUpsert = true});
        }

        protected Task UpsertMany<T>(List<T> insertObject) where T : IIdentifiable
        {
            var collection = CreateCollection<T>();
            var bulkOps = insertObject
                .Select(record => new ReplaceOneModel<T>(Builders<T>.Filter
                .Where(x => x.Id == record.Id), record) {IsUpsert = true})
                .Cast<WriteModel<T>>().ToList();
            return collection.BulkWriteAsync(bulkOps);
        }
    }

    public interface IIdentifiable
    {
        public string Id { get; }
    }
}