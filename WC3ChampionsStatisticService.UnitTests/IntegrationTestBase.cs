using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService;

namespace WC3ChampionsStatisticService.UnitTests
{
    public class IntegrationTestBase
    {
        protected readonly MongoClient MongoClient = new MongoClient("mongodb://176.28.16.249:3512/");

        [SetUp]
        public async Task Setup()
        {
            await MongoClient.DropDatabaseAsync("W3Champions-Statistic-Service");

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddMongoDbSetup(MongoClient);
        }
    }
}