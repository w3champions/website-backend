using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class PlayerRaceWins : IIdentifiable
    {
        public static PlayerRaceWins Create(string battleTag)
        {
            return new PlayerRaceWins
            {
                BattleTag = battleTag,
                WinLosses = new List<RaceWinLoss>
                {
                    new RaceWinLoss(Race.HU),
                    new RaceWinLoss(Race.OC),
                    new RaceWinLoss(Race.NE),
                    new RaceWinLoss(Race.UD),
                    new RaceWinLoss(Race.RnD)
                }
            };
        }

        public List<RaceWinLoss> WinLosses { get; set; }

        public string BattleTag { get; set; }

        public void RecordWin(Race race, bool won)
        {
            WinLosses.Single(w => w.Race == race).RecordWin(won);
        }

        public string Id => BattleTag;

        public int GetWinsPerRace(Race race)
        {
            return WinLosses.Single(w => w.Race == race).Wins;
        }

        public Race GetMainRace()
        {
            var mostGamesRace = WinLosses
                 .OrderByDescending(x => x.Games)
                 .FirstOrDefault();

            return mostGamesRace != null ? mostGamesRace.Race : Race.RnD;
        }
    }
}