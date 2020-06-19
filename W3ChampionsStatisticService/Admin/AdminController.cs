using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Admin
{

    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly IMatchRepository _matchRepository;

        public AdminController(IMatchRepository matchRepository)
        {
            _matchRepository = matchRepository;
        }

        [HttpGet("health-check")]
        public IActionResult HealthCheck()
        {
            return Ok();
        }

        [HttpGet("db-health-check")]
        public async Task<IActionResult> DatabaseHealthCheck()
        {
            var countOnGoingMatches = await _matchRepository.CountOnGoingMatches();
            return Ok(countOnGoingMatches);
        }
    }
}