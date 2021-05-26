using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.Achievements.Models {
    public class PlayerAchievements {
        [BsonId]
        public string PlayerId;
        public List<Achievement> playerAchievements;
    }
}