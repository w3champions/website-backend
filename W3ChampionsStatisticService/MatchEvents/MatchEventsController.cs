using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace W3ChampionsStatisticService.MatchEvents
{
    [ApiController]
    [Route("api/matchevents")]
    public class MatchEventsController : ControllerBase
    {
        private readonly InsertMatchEventsCommandHandler _handler;

        public MatchEventsController(InsertMatchEventsCommandHandler handler)
        {
            _handler = handler;
        }

        [HttpPost]
        public async Task<IActionResult> PushEvents(IList<MatchFinishedEvent> events)
        {
            await _handler.Insert(events);
            return Ok();
        }
    }
}