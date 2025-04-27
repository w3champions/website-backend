using System;
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
    public HeroType Id { get; set; } = HeroType.Unknown;

    [BsonIgnore()]
    public string Name
    {
        // Front End uses lowercase for translation keys
        get { return Enum.GetName(Id).ToLower(); }
    }

    /// <summary>
    /// For backwards compatibility to not break the frontend
    /// </summary>
    [BsonIgnore()]
    [JsonPropertyName("icon")]
    public string Icon
    {
        get { return Name; }
    }

    public int Level { get; set; }

    public Hero(W3C.Domain.MatchmakingService.Hero heroData)
    {
        Id = ParseHeroIcon(heroData.iconPath);
        Level = heroData.level;
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
        else if (Enum.TryParse(typeof(ReforgedHeroType), heroName, true, out var parsedReforgedId))
        {
            return MapReforged((ReforgedHeroType)parsedReforgedId);
        }
        else
        {
            // TODO: Add custom game mapping
            Log.Warning("Failed to parse {@iconPath} to a HeroId.", iconPath);
            return HeroType.Unknown;
        }
    }

    /// <summary>
    /// Maps Reforged heroes to the base heroes.
    /// </summary>
    public HeroType MapReforged(ReforgedHeroType reforgedHero)
    {
        switch (reforgedHero)
        {
            case ReforgedHeroType.JainaSea:
                return HeroType.Archmage;
            case ReforgedHeroType.ThrallChampion:
                return HeroType.Farseer;
            case ReforgedHeroType.FallenKingArthas:
                return HeroType.DeathKnight;
            case ReforgedHeroType.CenariusNightmare:
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
public class HeroFilter
{
    public HeroType Type { get; set; }
    public string Name { get; set; }
}

public enum ReforgedHeroType
{
    JainaSea,
    ThrallChampion,
    FallenKingArthas,
    CenariusNightmare,
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
}
