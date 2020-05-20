using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class PlayerProfileVnext
    {
        public static PlayerProfileVnext Create(string battleTag)
        {
            return new PlayerProfileVnext
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

        [BsonId]
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public List<Season> ParticipatedInSeasons  { get; set; } = new List<Season>();

        public List<RaceWinLoss> WinLosses { get; set; }

        public void RecordWin(Race race, int season, bool won)
        {
            if (!ParticipatedInSeasons.Select(s => s.Id).Contains(season))
            {
                ParticipatedInSeasons.Insert(0, new Season(season));
            }

            WinLosses.Single(w => w.Race == race).RecordWin(won);
        }

        public int GetWinsPerRace(Race race)
        {
            return WinLosses.Single(w => w.Race == race).Wins;
        }
    }
}