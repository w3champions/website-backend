using System.Threading.Tasks;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService;

namespace WC3ChampionsStatisticService.UnitTests
{
    public class IntegrationTestBase
    {
        protected readonly DbConnctionInfo DbConnctionInfo = new DbConnctionInfo("mongodb://176.28.16.249:3510/");

        [SetUp]
        public async Task Setup()
        {
            var client = new MongoClient(DbConnctionInfo.ConnectionString);
            await client.DropDatabaseAsync("W3Champions-Statistic-Service");
        }
    }
}