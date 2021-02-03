using System.Collections.Generic;

namespace W3ChampionsStatisticService.Achievements.Models {
    public class PlayerAchievements {
        public List<PlayerAchievement> MapAchievements { get; set; }
        public List<PlayerAchievement> TeamAchievements { get; set; }
    }

    public class PlayerAchievement {
        public string Title { get; set; }
        public string Caption { get; set; }
    }
}