using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class PlayerDetails
    {
        public string Id { get; set; }
        public string BattleTag { get; set; }

        public List<RaceWinLoss> WinLosses { get; set; }

        public PersonalSetting[] PersonalSettings { get; set; }

        public Race GetMainRace()
        {
            var mostGamesRace = WinLosses?
                 .OrderByDescending(x => x.Games)
                 .FirstOrDefault();

            return mostGamesRace != null ? mostGamesRace.Race : Race.RnD;
        }
    }
}
