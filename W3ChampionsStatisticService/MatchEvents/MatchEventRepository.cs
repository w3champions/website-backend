using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.MatchEvents
{
    public class MatchEventRepository : MongoDbRepositoryBase, IMatchEventRepository
    {
        private readonly ILogger<MatchEventRepository> _logger;

        public async Task<string> Insert(IList<MatchFinishedEvent> events)
        {
            if (!events.Any()) return ObjectId.Empty.ToString();
            var database = CreateClient();
            _logger.LogInformation("List not empty");

            var mongoCollection = database.GetCollection<MatchFinishedEvent>(nameof(MatchFinishedEvent));
            await mongoCollection.InsertManyAsync(events);
            _logger.LogInformation("Inserted");
            var insert = events.Last().Id.ToString();
            _logger.LogInformation($"Last ID was {insert}");
            return insert;
        }

        public async Task<List<MatchFinishedEvent>> Load(string lastObjectId = null, int pageSize = 100)
        {
            return await LoadSince<MatchFinishedEvent>(lastObjectId, pageSize);
        }

        public MatchEventRepository(DbConnctionInfo connectionInfo, ILogger<MatchEventRepository> logger = null) : base
        (connectionInfo)
        {
            _logger = logger ?? new Logger<MatchEventRepository>(new NullLoggerFactory());
        }
    }
}