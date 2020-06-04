using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerInfo
    {
        public string BattleTag { get; set; }

        public Race CalculatedRace { get; set; }

        public Race? SelectedRace { get; set; }

        public long? PictureId { get; set; }

        public string Country { get; set; }

        public string TwitchName { get; set; }

        public string ClanAbbrevation { get; set; }
    }
}
