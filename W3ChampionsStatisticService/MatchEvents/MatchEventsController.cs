using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.MatchEvents
{
    [ApiController]
    [Route("api")]
    public class MatchEventsController : ControllerBase
    {
        private readonly InsertMatchEventsCommandHandler _handler;
        private TrackingService _trackingService;

        public MatchEventsController(InsertMatchEventsCommandHandler handler, TrackingService trackingService)
        {
            _handler = handler;
            _trackingService = trackingService;
        }

        [HttpPost("matchEvents")]
        public async Task<IActionResult> PushEventsOld(
            string authorization,
            [FromBody] List<MatchFinishedEvent> events
            )
        {
            return await PushEvents(authorization, events);
        }

        [HttpPost("match-finished-events")]
        public async Task<IActionResult> PushEvents(
            string authorization,
            [FromBody] List<MatchFinishedEvent> events
        )
        {
            if (authorization != "D920618D-2296-4631-A6E4-333CCCDC04DE")
            {
                _trackingService.TrackUnauthorizedRequest(authorization, this);
                return Unauthorized("Sorry H4ckerb0i");
            }

            await _handler.Insert(events);
            return Ok();
        }

        [HttpPost("match-started-events")]
        public async Task<IActionResult> PushEvents(
            string authorization,
            [FromBody] List<MatchStartedEvent> events
        )
        {
            if (authorization != "D920618D-2296-4631-A6E4-333CCCDC04DE")
            {
                _trackingService.TrackUnauthorizedRequest(authorization, this);
                return Unauthorized("Sorry H4ckerb0i");
            }

            await _handler.Insert(events);
            return Ok();
        }

        [HttpPost("league-constellation-changed-events")]
        public async Task<IActionResult> PushLeagueChange(
            string authorization,
            [FromBody] List<LeagueConstellationChangedEvent> events
        )
        {
            if (authorization != "D920618D-2296-4631-A6E4-333CCCDC04DE")
            {
                _trackingService.TrackUnauthorizedRequest(authorization, this);
                return Unauthorized("Sorry H4ckerb0i");
            }

            await _handler.Insert(events);
            return Ok();
        }

        [HttpPost("ranking-changed-events")]
        public async Task<IActionResult> DivisionLeagueChanged(
            string authorization,
            [FromBody] List<RankingChangedEvent> events
        )
        {
            if (authorization != "D920618D-2296-4631-A6E4-333CCCDC04DE")
            {
                _trackingService.TrackUnauthorizedRequest(authorization, this);
                return Unauthorized("Sorry H4ckerb0i");
            }

            await _handler.Insert(events);
            return Ok();
        }
    }
}