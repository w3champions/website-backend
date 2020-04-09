using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public class MongoDbRepositoryBase
    {
        private readonly DbConnctionInfo _connectionInfo;
        private readonly string _databaseName = "W3Champions-Statistic-Service";

        public MongoDbRepositoryBase(DbConnctionInfo connectionInfo)
        {
            _connectionInfo = connectionInfo;
        }

        protected IMongoDatabase CreateClient()
        {
            var client = new MongoClient(_connectionInfo.ConnectionString);
            var database = client.GetDatabase(_databaseName);
            return database;
        }

        protected async Task<T> LoadFirst<T>(Expression<Func<T, bool>> expression)
        {
            var mongoCollection = CreateCollection<T>();
            var elements = await mongoCollection.FindAsync(expression);
            return elements.FirstOrDefault();
        }

        protected async Task<List<T>> LoadAll<T>()
        {
            var mongoCollection = CreateCollection<T>();
            var elements = await mongoCollection.Find(l => true).ToListAsync();
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
    }
}