using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace W3ChampionsStatisticService.Achievements
{
    [ApiController]
    [Route("api/achievements")]
    public class AchievementsController : ControllerBase
    {

        private readonly AchievementsEngine _achievementsEngine;
        
        public AchievementsController( AchievementsEngine achievementsEngine) {
            _achievementsEngine = achievementsEngine;
        }

        [HttpGet("{battleTag}")]
        public async Task<IActionResult> GetPlayerAchievements([FromRoute] string battleTag) {
            var achievementsEarned = await _achievementsEngine.Run(battleTag);
            return Ok(new {achievementsEarned});
        }
    }
}