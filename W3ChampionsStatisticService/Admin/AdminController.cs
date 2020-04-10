using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.Admin
{

    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly MongoClient _mongoClient;
        private readonly TrackingService _trackingService;
        private readonly string _databaseName = "W3Champions-Statistic-Service";

        public AdminController(MongoClient mongoClient, TrackingService trackingService)
        {
            _mongoClient = mongoClient;
            _trackingService = trackingService;
        }

        [HttpPut("reset-all")]
        public async Task<IActionResult> ResetAllReadModels(string authorization)
        {
            if (authorization != "ABD123F1-4AF5-4C55-B8D6-DCF7B5595991")
            {
                _trackingService.TrackUnauthorizedRequest(authorization, this);
                return Unauthorized("Sorry H4ckerb0i");
            }
            
            var database = _mongoClient.GetDatabase(_databaseName);
            var listCollections = (await database.ListCollections().ToListAsync());
            var collectionNames = listCollections.Select(c => c.Values.First().ToString());
            var allCollectionsExceptEvents = collectionNames.Where(c =>
                c != nameof(MatchFinishedEvent)
                && c != nameof(MatchStartedEvent)
                && c != nameof(LeagueConstellationChangedEvent)
                && c != nameof(RankingChangedEvent)
                );

            foreach (var collection in allCollectionsExceptEvents)
            {
                await database.DropCollectionAsync(collection);
            }

            return Ok();
        }

        [HttpGet("health-check")]
        public IActionResult HealtCheck()
        {
            return Ok();
        }
    }
}