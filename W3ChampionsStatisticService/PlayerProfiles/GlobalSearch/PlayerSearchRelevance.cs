using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.PlayerProfiles.GlobalSearch;

public class PlayerSearchRelevance(PersonalSetting p, int relevance)
{
    [BsonId]
    public PersonalSetting Player { get; set; } = p;
    public string RelevanceId { get; set; } = $"{relevance}_{p.Id}";
}
