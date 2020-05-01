using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Services;

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