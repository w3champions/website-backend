using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

namespace W3ChampionsStatistikSyncer.Controllers
{
    [ApiController]
    [Route("api/matchevents")]
    public class MatchEventsController : ControllerBase
    {
        [HttpPost]
        public IActionResult PushEvents(IEnumerable<dynamic> events)
        {
            return Ok();
        }
    }
}