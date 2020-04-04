using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using W3ChampionsStatisticService.MatchEvents;

namespace W3ChampionsStatisticService.Admin
{
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly DbConnctionInfo _connectionInfo;
        private readonly string _databaseName = "W3Champions-Statistic-Service";

        public AdminController(DbConnctionInfo connectionInfo)
        {
            _connectionInfo = connectionInfo;
        }

        [HttpPut("resetAll")]
        public async Task<IActionResult> GetLadder(string authorization)
        {
            if (authorization != "ABD123F1-4AF5-4C55-B8D6-DCF7B5595991") return Unauthorized("Sorry H4ckerb0i");
            var client = new MongoClient(_connectionInfo.ConnectionString);
            var database = client.GetDatabase(_databaseName);
            var listCollections = (await database.ListCollections().ToListAsync());
            var collectionNames = listCollections.Select(c => c.RawValues.First().ToString());
            var allCollectionsExceptEvents = collectionNames.Where(c => c != nameof(MatchFinishedEvent));

            foreach (var collection in allCollectionsExceptEvents)
            {
                await database.DropCollectionAsync(collection);
            }

            return Ok();
        }
    }
}