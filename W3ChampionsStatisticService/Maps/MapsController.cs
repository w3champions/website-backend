﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.WebApiCompatShim;
using System.Net.Http;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3C.Domain.MatchmakingService.MatchmakingContracts;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Maps
{
    [ApiController]
    [Route("api/maps")]
    public class MapsController : ControllerBase
    {
        private readonly MatchmakingServiceClient _matchmakingServiceClient;
        private readonly UpdateServiceClient _updateServiceClient;

        public MapsController(
            MatchmakingServiceClient matchmakingServiceClient,
            UpdateServiceClient updateServiceClient)
        {
            _matchmakingServiceClient = matchmakingServiceClient;
            _updateServiceClient = updateServiceClient;
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

        [HttpGet("{id}/files")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> GetMapFiles([FromQuery] int mapId)
        {
            var mapFiles = await _updateServiceClient.GetMapFiles(mapId);
            return Ok(mapFiles);
        }

        [HttpPost("{id}/files")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> CreateMapFile()
        {
            HttpRequestMessageFeature hreqmf = new HttpRequestMessageFeature(Request.HttpContext);
            var map = await _updateServiceClient.CreateMapFromFormAsync(hreqmf.HttpRequestMessage);
            return Ok(map);
        }

        [HttpGet("files/{fileId}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> GetMapFile(string fileId)
        {
            var mapFile = await _updateServiceClient.GetMapFile(fileId);
            return Ok(mapFile);
        }

        [HttpDelete("files/{fileId}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> DeleteMapFile(string fileId)
        {
            await _updateServiceClient.DeleteMapFile(fileId);
            return NoContent();
        }
    }
}
