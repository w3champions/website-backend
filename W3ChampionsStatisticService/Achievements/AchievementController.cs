using System;
using Microsoft.AspNetCore.Mvc;


namespace W3ChampionsStatisticService.Achievements {
    public class AchievementController : ControllerBase {
        private readonly AchievementRepositoryHandler _achievementRepositoryHandler;

        public AchievementController (AchievementRepositoryHandler achievementRepositoryHandler) {
            _achievementRepositoryHandler = achievementRepositoryHandler;
        }
    }
}