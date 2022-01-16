using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Admin.Portraits
{
    public class PortraitDefinition : IIdentifiable
    {
        public PortraitDefinition(int _id, List<string> _group = null)
        {
            Id = _id.ToString();
            Groups = _group;
        }
        [BsonId]
        public string Id { get; set; }
        public List<string> Groups { get; set; }

        public int getId()
        {
            return int.Parse(Id);
        }
    }
}
