using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class PlayerSearchInfo
    {
        [BsonId]
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public List<Season> Seasons
        {
          get
          {
            return this.ParticipatedInSeasons.OrderByDescending(s => s.Id).Take(3).ToList();
          }
        }
        public ProfilePicture ProfilePicture
        {
          get
          {
            if (this.PersonalSettings.Length > 0)
            {
              return this.PersonalSettings[0].ProfilePicture;
            }
            else
            {
              return ProfilePicture.Default();
            }
          }
        }

        [JsonIgnore]
        public List<Season> ParticipatedInSeasons { get; set; } = new List<Season>();
        [JsonIgnore]
        public List<PlayerInfo> PlayersInfo { get; set; } = new List<PlayerInfo>();
        [JsonIgnore]
        public PersonalSetting[] PersonalSettings { get; set; }

        public PlayerSearchInfo() {}
        public PlayerSearchInfo(PlayerOverallStats p)
        {
          BattleTag = p.BattleTag;
          Name = p.Name;
          ParticipatedInSeasons = p.ParticipatedInSeasons;
        }
    }
}
