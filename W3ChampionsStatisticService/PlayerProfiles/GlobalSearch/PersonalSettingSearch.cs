using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.PlayerProfiles.GlobalSearch
{
    public class PersonalSettingSearch
    {
        [BsonId]
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public ProfilePicture ProfilePicture { get; set; }
        public PlayerOverallStats[] PlayerStats;
    }
}
