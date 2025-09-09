using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.PlayerProfiles;
using W3C.Domain.Tracing;
using MongoDB.Bson;
using Serilog;

namespace W3ChampionsStatisticService.PersonalSettings;

[BsonIgnoreExtraElements]
public class PersonalSetting : IVersionable, IIdentifiable
{
    public PersonalSetting(string battleTag)
    {
        Id = battleTag;
        RaceWins = PlayerOverallStats.Create(Id);
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
    public SpecialPicture[] SpecialPictures { get; set; } = [];
    public string ChatAlias { get; set; }
    public List<ChatColor> ChatColors { get; set; } = [];
    public List<ChatIcon> ChatIcons { get; set; } = [];
    public ChatColor SelectedChatColor { get; set; }
    public List<ChatIcon> SelectedChatIcons { get; set; } = [];
    public AkaSettings AliasSettings { get; set; }

    [Trace]
    public bool SetProfilePicture(SetPictureCommand cmd)
    {
        bool isValid = false;

        switch (cmd.avatarCategory)
        {
            case AvatarCategory.Starter:
                isValid = true;
                break;

            case AvatarCategory.Special:
                isValid = SpecialPictures != null && SpecialPictures.Any(x => x.PictureId == cmd.pictureId);
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

        // If the currently selected profile picture is a Special portrait that is no longer available,
        // reset it to a Starter portrait via SetProfilePicture.
        if (ProfilePicture != null
            && ProfilePicture.Race == AvatarCategory.Special
            && (SpecialPictures == null || !SpecialPictures.Any(x => x.PictureId == ProfilePicture.PictureId)))
        {
            Log.Information("Resetting profile picture to Starter portrait for user {UserId}", Id);
            SetProfilePicture(new SetPictureCommand
            {
                avatarCategory = AvatarCategory.Starter,
                pictureId = 1,
                isClassic = false
            });
        }
    }

    public List<AvatarCategoryToMaxPictureId> PickablePictures =>
    [
        new(AvatarCategory.HU, GetMaxPictureIdForRace(Race.HU)),
        new(AvatarCategory.OC, GetMaxPictureIdForRace(Race.OC)),
        new(AvatarCategory.NE, GetMaxPictureIdForRace(Race.NE)),
        new(AvatarCategory.UD, GetMaxPictureIdForRace(Race.UD)),
        new(AvatarCategory.RnD, GetMaxPictureIdForRace(Race.RnD)),
        new(AvatarCategory.Total, GetMaxPictureIdForAllWins()),
    ];

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

    private void UpdateSelectedChatColor(ChatColor selectedChatColor)
    {
        if (selectedChatColor == null)
        {
            SelectedChatColor = null;
            return;
        }

        if (ChatColors == null)
        {
            ChatColors = [];
        }

        if (!ChatColors.Contains(selectedChatColor))
        {
            throw new InvalidOperationException("User does not own the selected chat color.");
        }

        SelectedChatColor = selectedChatColor;
    }

    private void UpdateSelectedChatIcons(List<ChatIcon> selectedChatIcons)
    {
        if (selectedChatIcons == null)
        {
            SelectedChatIcons = [];
            return;
        }

        if (ChatIcons.Intersect(selectedChatIcons).Count() != selectedChatIcons.Count)
        {
            throw new InvalidOperationException("User does not own the selected chat icons.");
        }

        SelectedChatIcons = [.. selectedChatIcons.Take(3)];
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

        UpdateSelectedChatColor(dto.SelectedChatColor);
        UpdateSelectedChatIcons(dto.SelectedChatIcons);

        AliasSettings = dto.AliasSettings;
    }

    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset LastUpdated { get; set; }

    [BsonIgnore]
    public List<WinsToPictureId> RacePictureRange =>
    [
        new(1, 5),
        new(2, 10),
        new(3, 25),
        new(4, 50),
        new(5, 100),
        new(6, 150),
        new(7, 250),
        new(8, 350),
        new(9, 500),
        new(10, 750),
        new(11, 1000),
        new(12, 1250),
        new(13, 1500),
        new(14, 1750),
        new(15, 2000),
        new(16, 2500),
        new(17, 3500),
        new(18, 5000)
    ];

    [BsonIgnore]
    public List<WinsToPictureId> TotalPictureRange =>
    [
        new(1, 15),
        new(2, 30),
        new(3, 75),
        new(4, 150),
        new(5, 300),
        new(6, 450),
        new(7, 750),
        new(8, 1000),
        new(9, 1500),
        new(10, 2250),
        new(11, 3000),
        new(12, 3750),
        new(13, 4500),
        new(14, 5250),
        new(15, 6000),
        new(16, 7500),
        new(17, 10000),
        new(18, 15000)
    ];

    [BsonIgnore]
    public List<WinsToPictureId> TournamentPictureRange =>

    // for future use with autotours
    [
        new(1, 1),
        new(2, 5),
        new(3, 10),
        new(4, 20),
        new(5, 30),
        new(6, 50),
        new(7, 75),
        new(8, 100),
        new(9, 125),
        new(10, 150),
        new(11, 200),
        new(12, 300),
        new(13, 400),
        new(14, 500),
        new(15, 750),
        new(16, 1000),
        new(17, 1250),
        new(18, 1500)
    ];
}
