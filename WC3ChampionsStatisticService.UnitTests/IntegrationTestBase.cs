using System.Threading.Tasks;
using MongoDB.Driver;
using NUnit.Framework;

namespace WC3ChampionsStatisticService.UnitTests
{
    public class IntegrationTestBase
    {
        protected readonly MongoClient MongoClient = new MongoClient("mongodb://176.28.16.249:3512/");

        [SetUp]
        public async Task Setup()
        {
            await MongoClient.DropDatabaseAsync("W3Champions-Statistic-Service");
        }
    }
}