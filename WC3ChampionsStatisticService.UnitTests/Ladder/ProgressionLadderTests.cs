using System.Collections.Generic;
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
public class ProgressionLadderTests : IntegrationTestBase
{
    private RankQueryHandler CreateQueryHandler()
    {
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);
        var clanRepository = new ClanRepository(MongoClient);
        var progressionRepository = new PlayerProgressionRepository(MongoClient);
        var progressionViewLoader = new ProgressionViewLoader(progressionRepository);
        var apexLeaderboardRepository = new ApexLeaderboardRepository(MongoClient);
        return new RankQueryHandler(
            rankRepository, playerRepository, clanRepository, progressionViewLoader,
            apexLeaderboardRepository, progressionRepository);
    }

    private async Task SeedProgression(
        string battleTag, int season, GameMode gameMode, Race? race, int league, int division, int points)
    {
        var repo = new PlayerProgressionRepository(MongoClient);
        var id = new BattleTagIdCombined(
            new List<PlayerId> { PlayerId.Create(battleTag) }, GateWay.Europe, gameMode, season, race);
        var p = PlayerProgression.Create(id);
        p.RecordRank(league, division, points, null);
        await repo.UpsertProgression(p);
    }

    private async Task SeedPlayerDetail(string battleTag, AvatarCategory pictureRace, long pictureId, string country)
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
    public async Task LoadProgressionLadder_ReturnsRankRows_PointsDesc_WithProgressionAndPlayersInfo()
    {
        // Adept I (league 2, division 1), three players, different points.
        await SeedProgression("low#1", 2, GameMode.GM_1v1, null, 2, 1, 10);
        await SeedProgression("high#2", 2, GameMode.GM_1v1, null, 2, 1, 90);
        await SeedProgression("mid#3", 2, GameMode.GM_1v1, null, 2, 1, 50);

        await SeedPlayerDetail("low#1", AvatarCategory.HU, 1, "BG");
        await SeedPlayerDetail("high#2", AvatarCategory.NE, 2, "US");
        await SeedPlayerDetail("mid#3", AvatarCategory.UD, 3, "DE");

        var queryHandler = CreateQueryHandler();

        var rows = await queryHandler.LoadProgressionLadder(2, GameMode.GM_1v1, 2, 1, null, 0, 100);

        Assert.AreEqual(3, rows.Count);

        // Points-desc order with RankNumber 1..3
        Assert.AreEqual("high#2", rows[0].PlayersInfo[0].BattleTag);
        Assert.AreEqual(1, rows[0].RankNumber);
        Assert.AreEqual("mid#3", rows[1].PlayersInfo[0].BattleTag);
        Assert.AreEqual(2, rows[1].RankNumber);
        Assert.AreEqual("low#1", rows[2].PlayersInfo[0].BattleTag);
        Assert.AreEqual(3, rows[2].RankNumber);

        // Progression view stamped from the same row
        Assert.IsNotNull(rows[0].Progression);
        Assert.AreEqual(2, rows[0].Progression.League);
        Assert.AreEqual(1, rows[0].Progression.Division);
        Assert.AreEqual(90, rows[0].Progression.Points);

        // PlayersInfo enriched (display data resolved from player-detail sources)
        Assert.AreEqual(AvatarCategory.NE, rows[0].PlayersInfo[0].SelectedRace);
        Assert.AreEqual(2, rows[0].PlayersInfo[0].PictureId);
        Assert.AreEqual("US", rows[0].PlayersInfo[0].Country);

        // RP-only field left at default (website renders progression, not RP)
        Assert.AreEqual(0, rows[0].RankingPoints);
    }

    [TestCase(0)] // Grand Master
    [TestCase(1)] // Master
    public async Task LoadProgressionLadder_ApexLeague_ReturnsEmpty(int apexLeague)
    {
        await SeedProgression("apex#1", 2, GameMode.GM_1v1, null, apexLeague, 1, 99);
        await SeedPlayerDetail("apex#1", AvatarCategory.HU, 1, "BG");

        var queryHandler = CreateQueryHandler();

        var rows = await queryHandler.LoadProgressionLadder(2, GameMode.GM_1v1, apexLeague, 1, null, 0, 100);

        Assert.IsEmpty(rows);
    }

    [Test]
    public async Task LoadProgressionLadder_AssignsGlobalRankNumber_AcrossPages()
    {
        // Five players in Adept I, points 100..60 desc.
        for (int i = 0; i < 5; i++)
        {
            await SeedProgression($"p{i}#1", 2, GameMode.GM_1v1, null, 2, 1, 100 - i * 10);
            await SeedPlayerDetail($"p{i}#1", AvatarCategory.HU, i + 1, "BG");
        }

        var queryHandler = CreateQueryHandler();

        // Second page: skip 1, take 2 -> points 90, 80 with GLOBAL ranks 2, 3.
        var page = await queryHandler.LoadProgressionLadder(2, GameMode.GM_1v1, 2, 1, null, 1, 2);

        Assert.AreEqual(2, page.Count);
        Assert.AreEqual(2, page[0].RankNumber);
        Assert.AreEqual(90, page[0].Progression.Points);
        Assert.AreEqual(3, page[1].RankNumber);
        Assert.AreEqual(80, page[1].Progression.Points);
    }

    [Test]
    public async Task LoadProgressionLadder_NoRows_ReturnsEmpty()
    {
        var queryHandler = CreateQueryHandler();

        var rows = await queryHandler.LoadProgressionLadder(99, GameMode.GM_1v1, 2, 1, null, 0, 100);

        Assert.IsEmpty(rows);
    }

    [Test]
    public async Task LoadProgressionLadder_FiltersByRace_WhenRaceProvided()
    {
        await SeedProgression("hu#1", 2, GameMode.GM_1v1, Race.HU, 2, 1, 80);
        await SeedProgression("ne#2", 2, GameMode.GM_1v1, Race.NE, 2, 1, 70);
        await SeedPlayerDetail("hu#1", AvatarCategory.HU, 1, "BG");
        await SeedPlayerDetail("ne#2", AvatarCategory.NE, 2, "US");

        var queryHandler = CreateQueryHandler();

        var rows = await queryHandler.LoadProgressionLadder(2, GameMode.GM_1v1, 2, 1, Race.HU, 0, 100);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("hu#1", rows[0].PlayersInfo[0].BattleTag);
        Assert.AreEqual(Race.HU, rows[0].Race);
    }
}
