using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.PlayerProfiles.GlobalSearch;

public class PlayerSearchRelevance
{
    [BsonId]
    public PersonalSetting Player { get; set; }
    public string RelevanceId { get; set; }

    public PlayerSearchRelevance(PersonalSetting p, int relevance)
    {
        Player = p;
        RelevanceId = $"{relevance}_{p.Id}";
    }
}
