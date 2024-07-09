using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Rewards.Portraits;

public class PortraitDefinition(int _id, List<string> _group = null) : IIdentifiable
{
    [BsonId]
    public string Id { get; set; } = _id.ToString();

    public List<string> Groups { get; set; } = _group;
}

public class SinglePortraitDefinitionAndGroup
{
    [BsonId]
    public string Id { get; set; }

    public string Groups { get; set; }

    public int getId()
    {
        return int.Parse(Id);
    }
}
public class PortraitGroup
{
    public string Group { get; set; }

    public List<int> PortraitIds { get; set; }
}
