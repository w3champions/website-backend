using Microsoft.AspNetCore.Mvc;

namespace W3ChampionsStatisticService.Admin
{

    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        [HttpGet("health-check")]
        public IActionResult HealtCheck()
        {
            return Ok();
        }
    }
}