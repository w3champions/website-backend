using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Achievements {

    [ApiController]
    [Route("api/achievements")]
    public class AchievementController : ControllerBase {
        private readonly AchievementRepositoryHandler _achievementRepositoryHandler;

        public AchievementController (AchievementRepositoryHandler achievementRepositoryHandler) {
            _achievementRepositoryHandler = achievementRepositoryHandler;
        }

        [HttpGet("{playerId}")]
        public async Task<IActionResult> GetPlayerAchievements(string playerId) {
            var response = await _achievementRepositoryHandler.GetPlayerAchievementsFromUI(playerId);
            return Ok(response);
        }
    }
}