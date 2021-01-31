using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;

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
        public void GetPlayerAchievements([FromRoute] string battleTag) => _achievementsEngine.Run(battleTag);
    }
}