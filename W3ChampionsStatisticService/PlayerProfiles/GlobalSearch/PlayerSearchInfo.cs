using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.PlayerProfiles.GlobalSearch;

public class PlayerSearchInfo(PersonalSetting p, string relevanceId)
{
    [BsonId]
    public string BattleTag { get; set; } = p.Id;
    public string Name { get; set; } = p.Id.Split("#")[0];
    public List<Season> Seasons { get; set; } = new List<Season>();
    public ProfilePicture ProfilePicture { get; set; } = p.ProfilePicture;
    public string RelevanceId { get; set; } = relevanceId;

    public void SetSeasons(PlayerOverallStats p)
    {
        Seasons = p
            .ParticipatedInSeasons
            .OrderByDescending(s => s.Id)
            .Take(3)
            .ToList();
    }
}
