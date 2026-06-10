using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Clans;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.Tests.Ladder;

[TestFixture]
public class ApexLeaderboardQueryTests : IntegrationTestBase
{
    private RankQueryHandler CreateQueryHandler()
    {
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);
        var clanRepository = new ClanRepository(MongoClient);
        var progressionViewLoader = new ProgressionViewLoader(new PlayerProgressionRepository(MongoClient));
        var apexLeaderboardRepository = new ApexLeaderboardRepository(MongoClient);
        var playerProgressionRepository = new PlayerProgressionRepository(MongoClient);
        return new RankQueryHandler(rankRepository, playerRepository, clanRepository, progressionViewLoader, apexLeaderboardRepository, playerProgressionRepository);
    }

    private async Task SeedPlayer(string battleTag, AvatarCategory pictureRace, long pictureId, string country)
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);

        await playerRepository.UpsertPlayer(new PlayerOverallStats { BattleTag = battleTag });
        await personalSettingsRepository.Save(new PersonalSetting(battleTag)
        {
            ProfilePicture = new ProfilePicture { Race = pictureRace, PictureId = pictureId },
            Country = country,
        });
    }

    [Test]
    public async Task LoadApexLeaderboard_ReturnsEnrichedRows_GmFirstInRankOrder_WithCutoffAndGmCount()
    {
        // Arrange: seed an ApexLeaderboard doc (2 GM league=0, 1 Master league=1) + matching player rows
        var apexRepository = new ApexLeaderboardRepository(MongoClient);
        await apexRepository.UpsertOne(new ApexLeaderboard
        {
            Id = "22_1",
            Season = 22,
            GameMode = GameMode.GM_1v1,
            CutoffApexPoints = 900,
            GmCount = 2,
            Players = new List<ApexLeaderboardEntry>
            {
                new() { BattleTags = new List<string> { "alpha#1" }, Race = Race.HU, ApexPoints = 1200, League = 0, RankNumber = 1 },
                new() { BattleTags = new List<string> { "beta#2" },  Race = Race.NE, ApexPoints = 1000, League = 0, RankNumber = 2 },
                new() { BattleTags = new List<string> { "gamma#3" }, Race = Race.UD, ApexPoints = 850,  League = 1, RankNumber = 3 },
            },
        });

        await SeedPlayer("alpha#1", AvatarCategory.HU, 5, "BG");
        await SeedPlayer("beta#2", AvatarCategory.NE, 7, "US");
        await SeedPlayer("gamma#3", AvatarCategory.UD, 9, "DE");

        var queryHandler = CreateQueryHandler();

        // Act
        var result = await queryHandler.LoadApexLeaderboard(22, GameMode.GM_1v1);

        // Assert: envelope cohort fields
        Assert.IsNotNull(result);
        Assert.AreEqual(900, result.CutoffApexPoints);
        Assert.AreEqual(2, result.GmCount);
        Assert.AreEqual(3, result.Players.Count);

        // GM-first in rankNumber order
        Assert.AreEqual(1, result.Players[0].RankNumber);
        Assert.AreEqual(0, result.Players[0].League);
        Assert.AreEqual(1200, result.Players[0].ApexPoints);
        Assert.AreEqual(2, result.Players[1].RankNumber);
        Assert.AreEqual(3, result.Players[2].RankNumber);
        Assert.AreEqual(1, result.Players[2].League);

        // Enrichment: PlayersInfo populated from battleTag → personal settings
        var top = result.Players[0];
        Assert.AreEqual(1, top.PlayersInfo.Count);
        Assert.AreEqual("alpha#1", top.PlayersInfo[0].BattleTag);
        Assert.AreEqual(AvatarCategory.HU, top.PlayersInfo[0].SelectedRace);
        Assert.AreEqual(5, top.PlayersInfo[0].PictureId);
        Assert.AreEqual("BG", top.PlayersInfo[0].Country);

        Assert.AreEqual("beta#2", result.Players[1].PlayersInfo[0].BattleTag);
        Assert.AreEqual("US", result.Players[1].PlayersInfo[0].Country);
    }

    [Test]
    public async Task LoadApexLeaderboard_PreservesAtTeamBattleTags()
    {
        var apexRepository = new ApexLeaderboardRepository(MongoClient);
        await apexRepository.UpsertOne(new ApexLeaderboard
        {
            Id = "22_6",
            Season = 22,
            GameMode = GameMode.GM_2v2_AT,
            CutoffApexPoints = 800,
            GmCount = 1,
            Players = new List<ApexLeaderboardEntry>
            {
                new() { BattleTags = new List<string> { "team-a#1", "team-a#2" }, Race = null, ApexPoints = 1100, League = 0, RankNumber = 1 },
            },
        });

        await SeedPlayer("team-a#1", AvatarCategory.HU, 1, "BG");
        await SeedPlayer("team-a#2", AvatarCategory.OC, 2, "PL");

        var queryHandler = CreateQueryHandler();

        var result = await queryHandler.LoadApexLeaderboard(22, GameMode.GM_2v2_AT);

        Assert.AreEqual(1, result.Players.Count);
        Assert.AreEqual(2, result.Players[0].PlayersInfo.Count);
        var tags = result.Players[0].PlayersInfo.Select(p => p.BattleTag).ToList();
        CollectionAssert.Contains(tags, "team-a#1");
        CollectionAssert.Contains(tags, "team-a#2");
    }

    [Test]
    public async Task LoadApexLeaderboard_ReturnsEmptyEnvelope_WhenNoDocForMode()
    {
        var queryHandler = CreateQueryHandler();

        var result = await queryHandler.LoadApexLeaderboard(99, GameMode.GM_1v1);

        Assert.IsNotNull(result);
        Assert.IsNull(result.CutoffApexPoints);
        Assert.AreEqual(0, result.GmCount);
        Assert.IsNotNull(result.Players);
        Assert.IsEmpty(result.Players);
    }
}
