using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class PersonalSetting
    {
        public PersonalSetting(string battleTag, List<PlayerProfileVnext> players = null)
        {
            Id = battleTag;
            Players = players ?? new List<PlayerProfileVnext>();
        }

        public string ProfileMessage { get; set; }
        [BsonIgnore]
        [JsonIgnore]
        public PlayerProfileVnext RaceWins => Players?.SingleOrDefault() ?? PlayerProfileVnext.Create(Id);
        public List<RaceWinLoss> WinLosses => RaceWins.WinLosses;
        [JsonIgnore]
        [BsonIgnoreIfNull]
        public List<PlayerProfileVnext> Players { get; set; }
        public string HomePage { get; set; }
        public ProfilePicture ProfilePicture { get; set; } = ProfilePicture.Default();
        public string Id { get; set; }

        public bool SetProfilePicture(Race race, long pictureId)
        {
            var winsPerRace = RaceWins?.GetWinsPerRace(race);
            if (winsPerRace >= PictureRange.FirstOrDefault(p => p.PictureId == pictureId)?.NeededWins)
            {
                ProfilePicture = new ProfilePicture(race, pictureId);
                return true;
            }

            return false;
        }

        public List<RaceToMaxPicture> PickablePictures => new List<RaceToMaxPicture>
        {
            new RaceToMaxPicture(Race.HU, GetMaxOf(RaceWins.GetWinsPerRace(Race.HU)) ),
            new RaceToMaxPicture(Race.OC, GetMaxOf(RaceWins.GetWinsPerRace(Race.OC)) ),
            new RaceToMaxPicture(Race.NE, GetMaxOf(RaceWins.GetWinsPerRace(Race.NE)) ),
            new RaceToMaxPicture(Race.UD, GetMaxOf(RaceWins.GetWinsPerRace(Race.UD)) ),
            new RaceToMaxPicture(Race.RnD, GetMaxOf(RaceWins.GetWinsPerRace(Race.RnD)) )
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
            new WinsToPictureId(3, 70),
            new WinsToPictureId(4, 150),
            new WinsToPictureId(5, 250),
            new WinsToPictureId(6, 400),
            new WinsToPictureId(7, 600),
            new WinsToPictureId(8, 900),
            new WinsToPictureId(9, 1200),
            new WinsToPictureId(10, 1500)
        };
    }
}