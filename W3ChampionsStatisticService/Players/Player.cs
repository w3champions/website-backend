using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.MatchEvents;

namespace W3ChampionsStatisticService.Players
{
    public class Player
    {
        [BsonId]
        public string BattleTag { get; set; }

        public Player(string battleTag)
        {
            BattleTag = battleTag;
        }

        public void UpdateProgress(MatchFinishedEvent nextEvent)
        {
        }
    }
}