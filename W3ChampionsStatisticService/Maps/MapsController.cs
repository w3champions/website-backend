using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3C.Domain.MatchmakingService.MatchmakingContracts;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Maps
{
    [ApiController]
    [Route("api/maps")]
    public class MapsController : ControllerBase
    {
        private readonly MatchmakingServiceClient _matchmakingServiceClient;

        public MapsController(MatchmakingServiceClient matchmakingServiceClient)
        {
            _matchmakingServiceClient = matchmakingServiceClient;
        }

        [HttpGet("")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> GetMaps([FromQuery] GetMapsRequest request)
        {
            var maps = await _matchmakingServiceClient.GetMaps(request);
            return Ok(maps);
        }

        [HttpPost("")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> CreateMap([FromBody] MapContract request)
        {
            var map = await _matchmakingServiceClient.CreateMap(request);
            return Ok(map);
        }

        [HttpPut("{id}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> CreateMap(int id, [FromBody] MapContract request)
        {
            var map = await _matchmakingServiceClient.UpdateMap(id, request);
            return Ok(map);
        }
    }
}
