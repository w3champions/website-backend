using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.PlayerProfiles.GlobalSearch;

public class PlayerSearchRelevance(PersonalSetting p, string relevanceId)
{
    [BsonId]
    public PersonalSetting Player { get; set; } = p;

    // Sort key and pagination cursor in one. Its shape depends on the search's context — see
    // PlayerService.RelevanceId.
    public string RelevanceId { get; set; } = relevanceId;
}
