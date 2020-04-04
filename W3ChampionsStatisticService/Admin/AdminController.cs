using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Admin
{
    [ApiController]
    [Route("api/ladder")]
    public class AdminController : ControllerBase
    {
        private readonly IPlayerRepository _playerRepository;
        private readonly IVersionRepository _versionRepository;

        public AdminController(
            IPlayerRepository playerRepository,
            IVersionRepository versionRepository)
        {
            _playerRepository = playerRepository;
            _versionRepository = versionRepository;
        }

        [HttpPut("resetAll")]
        public async Task<IActionResult> GetLadder(string authorization)
        {
            if (authorization != "ABD123F1-4AF5-4C55-B8D6-DCF7B5595991") return Unauthorized("Sorry H4ckerb0i");

            return Ok();
        }
    }
}