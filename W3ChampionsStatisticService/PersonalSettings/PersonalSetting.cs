using System;
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
            if (winsPerRace >= PictureRange.FirstOrDefault(p => p.PictureId == pictureId)?.NeededWins)
            {
                ProfilePicture = new ProfilePicture(race, pictureId);
                return true;
            }

            return false;
        }

        public List<RaceToMaxPicture> PickablePictures => new List<RaceToMaxPicture>
        {
            new RaceToMaxPicture(Race.HU, GetMaxOf(Player.GetWinsPerRace(Race.HU)) ),
            new RaceToMaxPicture(Race.OC, GetMaxOf(Player.GetWinsPerRace(Race.OC)) ),
            new RaceToMaxPicture(Race.NE, GetMaxOf(Player.GetWinsPerRace(Race.NE)) ),
            new RaceToMaxPicture(Race.UD, GetMaxOf(Player.GetWinsPerRace(Race.UD)) ),
            new RaceToMaxPicture(Race.RnD, GetMaxOf(Player.GetWinsPerRace(Race.RnD)) )
        };

        private long GetMaxOf(long getWinsPerRace)
        {
            return PictureRange.Where(r => r.NeededWins <= getWinsPerRace).Max(r => r.PictureId);
        }

        [BsonIgnore]
        public List<WinsToPictureId> PictureRange => new List<WinsToPictureId>
        {
            new WinsToPictureId(0, 0),
            new WinsToPictureId(1, 5),
            new WinsToPictureId(2, 20),
            new WinsToPictureId(3, 50),
            new WinsToPictureId(4, 120),
            new WinsToPictureId(5, 200),
            new WinsToPictureId(6, 300),
            new WinsToPictureId(7, 450),
            new WinsToPictureId(8, 600),
            new WinsToPictureId(9, 900),
            new WinsToPictureId(10, 1200)
        };
    }

    public class WinsToPictureId
    {
        public int PictureId { get; }
        public int NeededWins { get; }

        public WinsToPictureId(int pictureId, int neededWins)
        {
            PictureId = pictureId;
            NeededWins = neededWins;
        }
    }

    public class RaceToMaxPicture
    {
        public Race Race { get; }
        public long Max { get; }

        public RaceToMaxPicture(Race race, long max)
        {
            Race = race;
            Max = max;
        }
    }
}