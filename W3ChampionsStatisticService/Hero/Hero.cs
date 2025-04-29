using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Serilog;

namespace W3ChampionsStatisticService.Heroes;

/// <summary>
/// Used for MongoDB collection for Web Backend data.<br/>
/// This is the processed value from the MatchmakingService Hero provided in
/// the MatchFinishedEvent data.
/// </summary>
[BsonIgnoreExtraElements]
public class Hero
{
    [BsonRepresentation(BsonType.Int32)]
    public HeroType Id { get; set; }

    [BsonIgnore()]
    public string Name { get; private set; }

    /// <summary>
    /// For backwards compatibility to not break the frontend
    /// </summary>
    [BsonIgnore()]
    [JsonPropertyName("icon")]
    public string Icon => Name;

    public int Level { get; set; }

    // Don't need to store in MongoDB cos can infer from Id values. Whether hero data is for classic, reforged, custom, etc
    [BsonIgnore()]
    public HeroSource Source { get; private set; }

    public Hero(W3C.Domain.MatchmakingService.Hero heroData)
    {
        Id = ParseHeroIcon(heroData.iconPath);
        Level = heroData.level;
        Source = ParseHeroSource(Id);
        Name = ParseHeroName(Id, Source);
    }

    [BsonConstructor("Id", "Level")]
    public Hero(HeroType type, int level)
    {
        Id = type;
        Level = level;
        Source = ParseHeroSource(Id);
        Name = ParseHeroName(Id, Source);
    }

    /// <summary>
    /// Assumes that all icon values are file paths that can be parsed
    /// and that the hero name is in file name. <br/>
    ///
    /// Non-custom game icons are in the format.
    /// <code>
    /// UI/Glues/ScoreScreen/scorescreen-hero-{heroName}.blp
    ///
    /// WebUI/ScoreScreen/HeroIcons/scorescreen-hero-{heroName}.png
    /// </code>
    ///
    /// <returns>
    /// <see cref="HeroType"/> matching the hero name parsed from the icon path, or
    /// <see cref="HeroType.Unknown" /> if parsing failed.
    /// </returns>
    /// </summary>
    private HeroType ParseHeroIcon(string iconPath)
    {
        var iconFileName = Path.GetFileNameWithoutExtension(iconPath);
        var heroName = iconFileName.Split("-").Last();
        if (Enum.TryParse(typeof(HeroType), heroName, true, out var parsedHeroId))
        {
            return (HeroType)parsedHeroId;
        }
        else
        {
            // TODO: Add custom game mapping
            Log.Warning("Failed to parse {@iconPath} to a HeroId.", iconPath);
            return HeroType.Unknown;
        }
    }

    /// <summary>
    /// Parses a HeroType and HeroSource to determine the string name to use.
    /// Lowercase as the frontend uses the strings for translation keys and hero icon asset paths
    /// </summary>
    public static string ParseHeroName(HeroType type, HeroSource source)
    {
        var value = (int)type;
        switch (source)
        {
            case HeroSource.Unknown:
            case HeroSource.Classic:
                return Enum.GetName(type)?.ToLower();
            case HeroSource.Reforged:
                return Enum.GetName(MapReforged(type))?.ToLower();
            default:
                return Enum.GetName(HeroType.Unknown)?.ToLower();
        }
    }

    /// <summary>
    /// Parses a HeroType to determine the source based on the int value from HeroType. <br />
    /// Unknown: -1 <br />
    /// AllFilter: 0, used for query filtering and the frontend for dropdowns <br />
    /// Classic HeroTypes: 1-99 <br />
    /// Reforged HeroTypes: 100+ <br />
    /// </summary>
    public static HeroSource ParseHeroSource(HeroType type)
    {
        int value = (int)type;
        if (value > 0 && value < 100)
        {
            return HeroSource.Classic;
        }
        else if (value >= 100)
        {
            return HeroSource.Reforged;
        }
        else
        {
            return HeroSource.Unknown;
        }
    }

    /// <summary>
    /// Maps Reforged heroes to the base heroes.
    /// </summary>
    public static HeroType MapReforged(HeroType reforgedHero)
    {
        switch (reforgedHero)
        {
            case HeroType.JainaSea:
                return HeroType.Archmage;
            case HeroType.ThrallChampion:
                return HeroType.Farseer;
            case HeroType.FallenKingArthas:
                return HeroType.DeathKnight;
            case HeroType.CenariusNightmare:
                return HeroType.KeeperOfTheGrove;
            default:
                return HeroType.Unknown;
        }
    }
}

/// <summary>
/// KeyValue type used for the frontend to provide filtering, giving the enum value and string name.
/// The enum value is used in APIs, the string name is used in the frontend for icons, translations, etc.
/// </summary>
public class HeroFilter(HeroType type)
{
    public HeroType Type { get; } = type;
    public string Name { get; } = Enum.GetName(type)?.ToLower();

    public static List<HeroType> AllowedHeroTypes => Enum.GetValues<HeroType>().Where(hero => (int)hero >= 0 && (int)hero < 100).ToList();
}

/// <summary>
/// The source of where the hero data comes from, e.g. Classic, Reforged, Custom, etc
/// </summary>
public enum HeroSource
{
    Unknown,
    Classic,
    Reforged,
}

/// <summary>
/// Enum of heroes, value used as id
/// </summary>
public enum HeroType
{
    Unknown = -1,
    AllFilter,
    Archmage,
    Alchemist,
    AvatarOfFlame,
    BansheeRanger,
    Beastmaster,
    Blademaster,
    CryptLord,
    DeathKnight,
    DemonHunter,
    DreadLord,
    Farseer,
    KeeperOfTheGrove,
    Lich,
    MountainKing,
    Paladin,
    PandarenBrewmaster,
    PitLord,
    PriestessOfTheMoon,
    SeaWitch,
    ShadowHunter,
    Sorceror,
    TaurenChieftain,
    Tinker,
    Warden,

    // Reforged
    JainaSea = 100,
    ThrallChampion,
    FallenKingArthas,
    CenariusNightmare,
}
