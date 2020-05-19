using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerId
    {
        public static PlayerId Create(string nameTag)
        {
            return new PlayerId
            {
                Name = nameTag.Split("#")[0],
                BattleTag = nameTag
            };
        }

        public string Name { get; set; }
        public string BattleTag { get; set; }

        [BsonIgnore]
        public Race CalculatedRace { get; set; }

        [BsonIgnore]
        public Race? SelectedRace { get; set; }

        [BsonIgnore]
        public long? PictureId { get; set; }
    }
}