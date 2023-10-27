using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PersonalSettings;

public class PersonalSetting : IVersionable, IIdentifiable
{
    public PersonalSetting(string battleTag, PlayerOverallStats playerStats = null)
    {
        Id = battleTag;
        RaceWins = playerStats ?? PlayerOverallStats.Create(Id);
        AliasSettings = new AkaSettings();
    }

    public string ProfileMessage { get; set; }

    [JsonIgnore]
    [BsonIgnoreIfNull]
    public PlayerOverallStats RaceWins { get; set; }
    public List<RaceWinLoss> WinLosses => RaceWins.WinLosses;
    public string Twitch { get; set; }
    public string YouTube { get; set; }
    public string Twitter { get; set; }
    public string Trovo { get; set; }
    public string Douyu { get; set; }
    public string Country { get; set; }
    public string CountryCode { get; set; }
    public string Location { get; set; }
    public string HomePage { get; set; }
    public ProfilePicture ProfilePicture { get; set; } = ProfilePicture.Default();
    public string Id { get; set; }

    [BsonIgnoreIfNull]
    public SpecialPicture[] SpecialPictures { get; set; } = new SpecialPicture[0];
    public string ChatAlias { get; set; }
    public string ChatColor { get; set; }
    public AkaSettings AliasSettings { get; set; }

    public bool SetProfilePicture(SetPictureCommand cmd)
    {
        bool isValid = false;

        switch (cmd.avatarCategory)
        {
            case AvatarCategory.Starter:
                isValid = true;
                break;

            case AvatarCategory.Special:
                isValid = SpecialPictures == null ? false : SpecialPictures.Any(x => x.PictureId == cmd.pictureId);
                break;
            
            case AvatarCategory.Total:
                var totalWins = RaceWins?.GetTotalWins();
                isValid = totalWins >= TotalPictureRange.FirstOrDefault(p => p.PictureId == cmd.pictureId)?.NeededWins;
                break;
            
            default:
                var winsPerRace = RaceWins?.GetWinsPerRace((Race)cmd.avatarCategory);
                isValid = winsPerRace >= RacePictureRange.FirstOrDefault(p => p.PictureId == cmd.pictureId)?.NeededWins;
                break;
        }

        if (isValid)
        {
            ProfilePicture = new ProfilePicture()
            {
                Race = cmd.avatarCategory,
                PictureId = cmd.pictureId,
                IsClassic = cmd.isClassic
            };
        }

        return isValid;
    }

    public void UpdateSpecialPictures(SpecialPicture[] specialPictures)
    {
        SpecialPictures = specialPictures;
    }

    public List<AvatarCategoryToMaxPictureId> PickablePictures => new List<AvatarCategoryToMaxPictureId>
    {
        new AvatarCategoryToMaxPictureId(AvatarCategory.HU, GetMaxPictureIdForRace(Race.HU)),
        new AvatarCategoryToMaxPictureId(AvatarCategory.OC, GetMaxPictureIdForRace(Race.OC)),
        new AvatarCategoryToMaxPictureId(AvatarCategory.NE, GetMaxPictureIdForRace(Race.NE)),
        new AvatarCategoryToMaxPictureId(AvatarCategory.UD, GetMaxPictureIdForRace(Race.UD)),
        new AvatarCategoryToMaxPictureId(AvatarCategory.RnD, GetMaxPictureIdForRace(Race.RnD)),
        new AvatarCategoryToMaxPictureId(AvatarCategory.Total, GetMaxPictureIdForAllWins()),
    };

    private long GetMaxPictureIdForRace(Race race)
    {
        var minimumWinsNeededForRaceIcon = RacePictureRange.First().NeededWins;
        var raceWinsForRace = RaceWins.GetWinsPerRace(race);

        if (raceWinsForRace < minimumWinsNeededForRaceIcon) return 0;

        return RacePictureRange
            .Where(r => r.NeededWins <= raceWinsForRace)
            .Max(r => r.PictureId);
    }

    private long GetMaxPictureIdForAllWins()
    {

        var minimumWinsNeededForAllIcon = TotalPictureRange.First().NeededWins;
        var raceWinsForAll = RaceWins.GetTotalWins();

        if (raceWinsForAll < minimumWinsNeededForAllIcon) return 0;
        return TotalPictureRange
            .Where(r => r.NeededWins <= RaceWins.GetTotalWins())
            .Max(r => r.PictureId);
    }

    public void Update(PersonalSettingsDTO dto)
    {
        ProfileMessage = dto.ProfileMessage;
        Twitch = dto.Twitch;
        YouTube = dto.Youtube;
        Twitter = dto.Twitter;
        Trovo = dto.Trovo;
        Douyu = dto.Douyu;
        HomePage = dto.HomePage;
        Country = dto.Country;
        CountryCode = dto.CountryCode;
        AliasSettings = dto.AliasSettings;
    }

    public DateTimeOffset LastUpdated { get; set; }

    [BsonIgnore]
    public List<WinsToPictureId> RacePictureRange => new List<WinsToPictureId>
    {
        new WinsToPictureId(1, 5),
        new WinsToPictureId(2, 10),
        new WinsToPictureId(3, 25),
        new WinsToPictureId(4, 50),
        new WinsToPictureId(5, 100),
        new WinsToPictureId(6, 150),
        new WinsToPictureId(7, 250),
        new WinsToPictureId(8, 350),
        new WinsToPictureId(9, 500),
        new WinsToPictureId(10, 750),
        new WinsToPictureId(11, 1000),
        new WinsToPictureId(12, 1250),
        new WinsToPictureId(13, 1500),
        new WinsToPictureId(14, 1750),
        new WinsToPictureId(15, 2000),
        new WinsToPictureId(16, 2500),
        new WinsToPictureId(17, 3500),
        new WinsToPictureId(18, 5000)
    };

    [BsonIgnore]
    public List<WinsToPictureId> TotalPictureRange => new List<WinsToPictureId>
    {
        new WinsToPictureId(1, 15),
        new WinsToPictureId(2, 30),
        new WinsToPictureId(3, 75),
        new WinsToPictureId(4, 150),
        new WinsToPictureId(5, 300),
        new WinsToPictureId(6, 450),
        new WinsToPictureId(7, 750),
        new WinsToPictureId(8, 1000),
        new WinsToPictureId(9, 1500),
        new WinsToPictureId(10, 2250),
        new WinsToPictureId(11, 3000),
        new WinsToPictureId(12, 3750),
        new WinsToPictureId(13, 4500),
        new WinsToPictureId(14, 5250),
        new WinsToPictureId(15, 6000),
        new WinsToPictureId(16, 7500),
        new WinsToPictureId(17, 10000),
        new WinsToPictureId(18, 15000)
    };

    [BsonIgnore]
    public List<WinsToPictureId> TournamentPicturerange => new List<WinsToPictureId>
    // for future use with autotours
    {
        new WinsToPictureId(1, 1),
        new WinsToPictureId(2, 5),
        new WinsToPictureId(3, 10),
        new WinsToPictureId(4, 20),
        new WinsToPictureId(5, 30),
        new WinsToPictureId(6, 50),
        new WinsToPictureId(7, 75),
        new WinsToPictureId(8, 100),
        new WinsToPictureId(9, 125),
        new WinsToPictureId(10, 150),
        new WinsToPictureId(11, 200),
        new WinsToPictureId(12, 300),
        new WinsToPictureId(13, 400),
        new WinsToPictureId(14, 500),
        new WinsToPictureId(15, 750),
        new WinsToPictureId(16, 1000),
        new WinsToPictureId(17, 1250),
        new WinsToPictureId(18, 1500)
    };
}
