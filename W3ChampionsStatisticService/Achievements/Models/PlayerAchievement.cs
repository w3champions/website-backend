using System.Collections.Generic;

namespace W3ChampionsStatisticService.Achievements.Models {
    public class PlayerAchievements {
        public List<PlayerAchievement> MapAchievements { get; set; }
        public List<PlayerAchievement> TeamAchievements { get; set; }
    }
    
    //TODO: Added new collection to DB with one document.... working here.. need to add new achievement types "map" and "player"

    public class PlayerAchievement {
        public string Type { get; set; }
        public string Title { get; set; }
        public string Caption { get; set; }

        public string Map { get; set; }

        public string[] Partners { get; set; }
    }
}