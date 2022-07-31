using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles.War3InfoPlayerAkas;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerInfo
    {
        public string BattleTag { get; set; }

        public Race CalculatedRace { get; set; }

        public AvatarCategory? SelectedRace { get; set; }

        public long? PictureId { get; set; }

        public bool isClassicPicture { get; set; }

        public string Country { get; set; }

        public string CountryCode { get; set; }

        public string Location { get; set; }

        public string TwitchName { get; set; }

        public string ClanId { get; set; }

        public Player PlayerAkaData { get; set; }
    }
}
