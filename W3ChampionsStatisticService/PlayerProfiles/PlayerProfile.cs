using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class PlayerProfile
    {
        public static PlayerProfile Create(string battleTag)
        {
            return new PlayerProfile
            {
                Name = battleTag.Split("#")[0],
                BattleTag = battleTag,
            };
        }

        [BsonId]
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public List<Season> ParticipatedInSeasons  { get; set; } = new List<Season>();

        public void RecordWin(Race race, GameMode mode, GateWay gateWay, int season, bool won)
        {
            if (!ParticipatedInSeasons.Select(s => s.Id).Contains(season))
            {
                ParticipatedInSeasons.Insert(0, new Season(season));
            }
        }
    }
}