using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace W3ChampionsStatisticService.MatchEvents
{
    [ApiController]
    [Route("api/matchevents")]
    public class MatchEventsController : ControllerBase
    {
        private readonly InsertMatchEventsCommandHandler _handler;
        private readonly ILogger<InsertMatchEventsCommandHandler> _logger;

        public MatchEventsController(InsertMatchEventsCommandHandler handler, ILogger<InsertMatchEventsCommandHandler> logger)
        {
            _handler = handler;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> PushEvents(
            string authorization,
            [FromBody] IList<MatchFinishedEvent> events
            )
        {
            _logger.LogInformation($"Entered with {JsonConvert.SerializeObject(events)}");
            if (authorization != "D920618D-2296-4631-A6E4-333CCCDC04DE") return Unauthorized("Sorry H4ckerb0i");
            await _handler.Insert(events);
            _logger.LogInformation($"Inserted {events.Count}");
            return Ok();
        }
    }
}