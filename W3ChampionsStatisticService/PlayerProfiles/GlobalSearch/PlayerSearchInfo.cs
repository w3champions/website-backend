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

        public PlayerSearchInfo(PersonalSetting p)
        {
          BattleTag = p.Id;
          Name = p.Id.Split("#")[0];
          Seasons = new List<Season>();
          ProfilePicture = p.ProfilePicture;
        }

        public void SetSeasons(PlayerOverallStats p)
        {
          Seasons = p
            .ParticipatedInSeasons
            .OrderByDescending(s => s.Id)
            .Take(3)
            .ToList();
        }
    }
}
