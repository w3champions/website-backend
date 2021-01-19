using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerProfiles.War3InfoPlayerAkas;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class PlayerOverallStats
    {
        [BsonId]
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public List<Season> ParticipatedInSeasons  { get; set; } = new List<Season>();
        public List<RaceWinLoss> WinLosses { get; set; }
        public Player PlayerAkaData { get; set; }

        public static PlayerOverallStats Create(string battleTag)
        {
            return new PlayerOverallStats
            {
                Name = battleTag.Split("#")[0],
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

        public void RecordWin(Race race, int season, bool won)
        {
            if (!ParticipatedInSeasons.Select(s => s.Id).Contains(season))
            {
                ParticipatedInSeasons.Insert(0, new Season(season));
            }

            if (season != 0)
            {
                WinLosses.Single(w => w.Race == race).RecordWin(won);
            }
        }

        public int GetWinsPerRace(Race race)
        {
            return WinLosses.Single(w => w.Race == race).Wins;
        }
    }
}