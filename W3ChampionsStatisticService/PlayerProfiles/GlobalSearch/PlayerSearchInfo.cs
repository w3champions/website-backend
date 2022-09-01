using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.PlayerProfiles.GlobalSearch
{
    public class PlayerSearchInfo
    {
        [BsonId]
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public List<Season> Seasons { get; set; }
        public ProfilePicture ProfilePicture { get; set; }

        public PlayerSearchInfo(PersonalSettingSearch p)
        {
          BattleTag = p.BattleTag;
          Name = p.BattleTag.Split("#")[0];
          if (p.PlayerStats.Length > 0) {
            Seasons = p.PlayerStats[0]
                       .ParticipatedInSeasons
                       .OrderByDescending(s => s.Id)
                       .Take(3)
                       .ToList();
          } else {
            Seasons = new List<Season>();
          }
          ProfilePicture = p.ProfilePicture;
        }
    }
}
