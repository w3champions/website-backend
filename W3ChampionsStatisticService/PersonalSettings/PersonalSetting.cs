using System.Collections.Generic;
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
        public string HomePage { get; set; }
        public ProfilePicture ProfilePicture { get; set; } = ProfilePicture.Default();
        public string Id { get; set; }

        public bool SetProfilePicture(PlayerProfile player, Race race, long pictureId)
        {
            var winsPerRace = player.GetWinsPerRace(race);
            if (winsPerRace >= PictureRange[pictureId])
            {
                ProfilePicture = new ProfilePicture(race, pictureId);
                return true;
            }

            return false;
        }

        private Dictionary<long, long> PictureRange = new Dictionary<long, long>
        {
            {0, 0},
            {1, 5},
            {2, 20},
            {3, 75},
            {4, 200},
            {5, 500},
            {6, 1000},
        };
    }
}