using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Achievements.Models {
    public class PlayerAchievements {
        [BsonId]
        public string PlayerId {get; set;}
        public List<Achievement> PlayerAchievementList {get; set;}
    }
}