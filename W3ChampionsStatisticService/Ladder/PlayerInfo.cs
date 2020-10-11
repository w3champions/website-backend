using W3ChampionsStatisticService.CommonValueObjects;

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

        public string Location { get; set; }

        public string TwitchName { get; set; }

        public string ClanId { get; set; }
    }
}
