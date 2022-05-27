using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.PadEvents.MatchmakingContracts;

namespace W3ChampionsStatisticService.Maps
{
    [ApiController]
    [Route("api/maps")]
    public class MapsController : ControllerBase
    {
        private readonly MatchmakingServiceRepo _matchmakingServiceRepository;

        public MapsController(MatchmakingServiceRepo matchmakingServiceRepository)
        {
            _matchmakingServiceRepository = matchmakingServiceRepository;
        }

        [HttpGet("")]
        public async Task<IActionResult> GetMaps([FromQuery] GetMapsRequest request)
        {
            var maps = await _matchmakingServiceRepository.GetMaps(request);
            return Ok(maps);
        }

        [HttpPost("")]
        public async Task<IActionResult> CreateMap([FromBody] MapContract request)
        {
            var map = await _matchmakingServiceRepository.CreateMap(request);
            return Ok(map);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> CreateMap(int id, [FromBody] MapContract request)
        {
            var map = await _matchmakingServiceRepository.UpdateMap(id, request);
            return Ok(map);
        }
    }
}
