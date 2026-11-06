using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.PersonalSettings.Hotkeys;

[BsonIgnoreExtraElements]
public class HotkeySetup : IIdentifiable
{
    /// <summary>The owner's battleTag.</summary>
    public string Id { get; set; }

    public int Mode { get; set; }

    public string CustomKeys { get; set; }

    public string PreferenceSettings { get; set; }

    public long Revision { get; set; }

    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset UpdatedAt { get; set; }
}
