using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;


namespace W3ChampionsStatisticService.Achievements {
    public class AchievementController : ControllerBase {
        private readonly AchievementRepositoryHandler _achievementRepositoryHandler;

        public AchievementController (AchievementRepositoryHandler achievementRepositoryHandler) {
            _achievementRepositoryHandler = achievementRepositoryHandler;
        }

        [HttpGet("{playerId}")]
        public async Task<IActionResult> GetPlayerAchievements(string playerId) {
            var response = await _achievementRepositoryHandler.GetPlayerAchievements(playerId);
            return Ok(response);
        }
    }
}