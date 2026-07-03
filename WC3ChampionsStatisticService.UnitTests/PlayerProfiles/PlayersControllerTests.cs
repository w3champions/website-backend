using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Clans;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.ChatDetails;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles;

[TestFixture]
public class PlayersControllerTests
{
    private const string BattleTag = "peter#123";

    private Mock<IClanRepository> _clanRepo;
    private Mock<IPersonalSettingsRepository> _settingsRepo;
    private Mock<IMatchRepository> _matchRepo;
    private Mock<IRankRepository> _rankRepo;
    private Mock<IPlayerRepository> _playerRepo;

    [SetUp]
    public void SetUp()
    {
        _clanRepo = new Mock<IClanRepository>();
        _settingsRepo = new Mock<IPersonalSettingsRepository>();
        _matchRepo = new Mock<IMatchRepository>();
        _rankRepo = new Mock<IRankRepository>();
        _playerRepo = new Mock<IPlayerRepository>();

        // default: current season 5, no games, no ranks
        _matchRepo.Setup(m => m.LoadLastSeason()).ReturnsAsync(new Season(5));
        _playerRepo.Setup(p => p.LoadGameModeStatPerGateway(BattleTag, 5))
            .ReturnsAsync(new List<PlayerGameModeStatPerGateway>());
        _rankRepo.Setup(r => r.LoadRanksForPlayers(It.IsAny<List<string>>(), 5))
            .ReturnsAsync(new List<Rank>());
        _rankRepo.Setup(r => r.LoadLeagueConstellation(5))
            .ReturnsAsync(new List<LeagueConstellation>());
    }

    private PlayersController CreateController()
    {
        var handler = new ChatDetailsQueryHandler(_matchRepo.Object, _rankRepo.Object, _playerRepo.Object);
        return new PlayersController(
            _playerRepo.Object,
            null, // GameModeStatQueryHandler — not used by clan-and-picture
            _settingsRepo.Object,
            _clanRepo.Object,
            null, // PlayerAkaProvider — not used by clan-and-picture
            null, // PlayerService — not used by clan-and-picture
            Mock.Of<IBattleTagResolver>(),
            handler);
    }

    private static ChatDetailsDto GetDto(IActionResult result)
    {
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        return (ChatDetailsDto)((OkObjectResult)result).Value;
    }

    [Test]
    public async Task GetClanAndPicture_LegacyFields_UnchangedForClanPlayerWithSettings()
    {
        var settings = new PersonalSetting(BattleTag)
        {
            ProfilePicture = ProfilePicture.Default(),
            SelectedChatColor = new ChatColor("chat_color_purple"),
            SelectedChatIcons = new List<ChatIcon> { new ChatIcon("chat_icon_crown") },
        };
        _clanRepo.Setup(c => c.LoadMemberShip(BattleTag))
            .ReturnsAsync(new ClanMembership { BattleTag = BattleTag, ClanId = "W3C" });
        _settingsRepo.Setup(s => s.Load(BattleTag)).ReturnsAsync(settings);

        var dto = GetDto(await CreateController().GetClanAndPicture(BattleTag));

        Assert.That(dto.ClanId, Is.EqualTo("W3C"));
        Assert.That(dto.ProfilePicture, Is.SameAs(settings.ProfilePicture));
        Assert.That(dto.ChatColor.ColorId, Is.EqualTo("chat_color_purple"));
        Assert.That(dto.ChatIcons.Single().IconId, Is.EqualTo("chat_icon_crown"));
    }

    [Test]
    public async Task GetClanAndPicture_UnknownPlayer_Returns200WithDefaultsAndEmptyEnrichment()
    {
        _clanRepo.Setup(c => c.LoadMemberShip(BattleTag)).ReturnsAsync((ClanMembership)null);
        _settingsRepo.Setup(s => s.Load(BattleTag)).ReturnsAsync((PersonalSetting)null);

        var dto = GetDto(await CreateController().GetClanAndPicture(BattleTag));

        Assert.That(dto.ClanId, Is.Null);
        Assert.That(dto.ProfilePicture, Is.Not.Null); // ProfilePicture.Default() fallback, unchanged behavior
        Assert.That(dto.ChatColor, Is.Null);
        Assert.That(dto.ChatIcons, Is.Null);
        Assert.That(dto.Rank, Is.Null);
        Assert.That(dto.GamesPlayed, Is.EqualTo(0));
        Assert.That(dto.Season, Is.EqualTo(5));
    }

    [Test]
    public async Task GetClanAndPicture_RankedPlayer_CarriesRankGamesAndSeason()
    {
        _clanRepo.Setup(c => c.LoadMemberShip(BattleTag)).ReturnsAsync((ClanMembership)null);
        _settingsRepo.Setup(s => s.Load(BattleTag)).ReturnsAsync((PersonalSetting)null);
        _playerRepo.Setup(p => p.LoadGameModeStatPerGateway(BattleTag, 5)).ReturnsAsync(
            new List<PlayerGameModeStatPerGateway>
            {
                new PlayerGameModeStatPerGateway { Wins = 2, Losses = 1 },
                new PlayerGameModeStatPerGateway { Wins = 4, Losses = 3 },
            });
        _rankRepo.Setup(r => r.LoadRanksForPlayers(
                It.Is<List<string>>(l => l.Count == 1 && l[0] == BattleTag), 5))
            .ReturnsAsync(new List<Rank>
            {
                new Rank(new List<string> { BattleTag }, 3, 14, 100, null, GateWay.Europe, GameMode.GM_1v1, 5),
            });
        _rankRepo.Setup(r => r.LoadLeagueConstellation(5)).ReturnsAsync(new List<LeagueConstellation>
        {
            new LeagueConstellation(5, GateWay.Europe, GameMode.GM_1v1,
                new List<League> { new League(3, 5, "Diamond", 2) }),
        });

        var dto = GetDto(await CreateController().GetClanAndPicture(BattleTag));

        Assert.That(dto.GamesPlayed, Is.EqualTo(10));
        Assert.That(dto.Season, Is.EqualTo(5));
        Assert.That(dto.Rank.LeagueId, Is.EqualTo(3));
        Assert.That(dto.Rank.LeagueName, Is.EqualTo("Diamond"));
        Assert.That(dto.Rank.LeagueOrder, Is.EqualTo(5));
        Assert.That(dto.Rank.LeagueDivision, Is.EqualTo(2));
        Assert.That(dto.Rank.RankNumber, Is.EqualTo(14));
        Assert.That(dto.Rank.GameMode, Is.EqualTo(GameMode.GM_1v1));
        Assert.That(dto.Rank.GateWay, Is.EqualTo(GateWay.Europe));
    }

    [Test]
    public void ChatDetailsDto_WireShape_LegacyKeysByteCompatible_NewKeysAdditive()
    {
        var dto = new ChatDetailsDto("W3C", ProfilePicture.Default(),
            new ChatColor("chat_color_purple"),
            new List<ChatIcon> { new ChatIcon("chat_icon_crown") },
            new ChatRank(3, "Diamond", 5, 2, 14, GameMode.GM_1v1, GateWay.Europe),
            gamesPlayed: 42, season: 22);

        // ASP.NET Core's serializer defaults (camelCase web options, enums as numbers)
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        foreach (var legacyKey in new[] { "clanId", "profilePicture", "chatColor", "chatIcons" })
        {
            Assert.That(root.TryGetProperty(legacyKey, out _), Is.True, $"missing legacy key '{legacyKey}'");
        }
        // contract-§4 prose names must NOT replace the existing wire names (pre-resolved decision)
        Assert.That(root.TryGetProperty("selectedChatColor", out _), Is.False);
        Assert.That(root.TryGetProperty("selectedChatIcons", out _), Is.False);

        Assert.That(root.GetProperty("gamesPlayed").GetInt32(), Is.EqualTo(42));
        Assert.That(root.GetProperty("season").GetInt32(), Is.EqualTo(22));
        var rank = root.GetProperty("rank");
        Assert.That(rank.GetProperty("leagueId").GetInt32(), Is.EqualTo(3));
        Assert.That(rank.GetProperty("leagueName").GetString(), Is.EqualTo("Diamond"));
        Assert.That(rank.GetProperty("leagueOrder").GetInt32(), Is.EqualTo(5));
        Assert.That(rank.GetProperty("leagueDivision").GetInt32(), Is.EqualTo(2));
        Assert.That(rank.GetProperty("rankNumber").GetInt32(), Is.EqualTo(14));
        Assert.That(rank.GetProperty("gameMode").GetInt32(), Is.EqualTo(1));  // enums serialize as numbers
        Assert.That(rank.GetProperty("gateWay").GetInt32(), Is.EqualTo(20));
    }

    [Test]
    public void ChatDetailsDto_LegacyFourArgConstruction_StillCompiles_AndEmitsNullRank()
    {
        // the old chat-service era construction must keep compiling (additive defaults)
        var dto = new ChatDetailsDto(null, ProfilePicture.Default(), null, null);

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.GetProperty("rank").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(doc.RootElement.GetProperty("gamesPlayed").GetInt32(), Is.EqualTo(0));
        Assert.That(doc.RootElement.GetProperty("season").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }
}
