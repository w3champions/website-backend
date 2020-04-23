using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class PersonalSetting
    {
        public PersonalSetting(string battleTag)
        {
            Id = battleTag;
        }

        public string ProfileMessage { get; set; }
        [BsonIgnore]
        [JsonIgnore]
        public PlayerProfile Player => Players?.SingleOrDefault() ?? PlayerProfile.Create(Id, Id.Split("@")[0]);
        [JsonIgnore]
        public List<PlayerProfile> Players { get; set; }
        public string HomePage { get; set; }
        public ProfilePicture ProfilePicture { get; set; } = ProfilePicture.Default();
        public string Id { get; set; }

        public bool SetProfilePicture(Race race, long pictureId)
        {
            var winsPerRace = Player.GetWinsPerRace(race);
            if (winsPerRace >= PictureRange[pictureId])
            {
                ProfilePicture = new ProfilePicture(race, pictureId);
                return true;
            }

            return false;
        }

        private Dictionary<long, long> PictureRange => new Dictionary<long, long>
        {
            {0, 0},
            {1, 5},
            {2, 20},
            {3, 50},
            {4, 120},
            {5, 200},
            {6, 300},
            {7, 450},
            {8, 600},
            {9, 900},
            {10, 1200},
        };
    }
}