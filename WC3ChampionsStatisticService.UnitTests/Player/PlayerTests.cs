using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;
using W3ChampionsStatisticService.PlayerProfiles.RaceStats;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.Ladder;

namespace WC3ChampionsStatisticService.Tests.Player;

[TestFixture]
public class PlayerTests : IntegrationTestBase
{
    [Test]
    public async Task LoadAndSave()
    {
        var playerRepository = new PlayerRepository(MongoClient);

        var player = PlayerOverallStats.Create("peter#123");
        await playerRepository.UpsertPlayer(player);
        var playerLoaded = await playerRepository.LoadPlayerOverallStats(player.BattleTag);

        Assert.AreEqual(player.BattleTag, playerLoaded.BattleTag);
    }

    [Test]
    public async Task Search()
    {
        var playerRepository = new PlayerRepository(MongoClient);

        var player = PlayerOverallStats.Create("Peter#123");
        var player2 = PlayerOverallStats.Create("Anderer#123");
        await playerRepository.UpsertPlayer(player);
        await playerRepository.UpsertPlayer(player2);
        var playerLoaded = await playerRepository.SearchForPlayer("ete");

        Assert.AreEqual(player.BattleTag, playerLoaded[0].BattleTag);
        Assert.AreEqual(1, playerLoaded.Count);
    }

    [Test]
    public async Task Search_DuplicateNameBug()
    {
        var playerRepository = new PlayerRepository(MongoClient);

        var player = PlayerOverallStats.Create("ThunderHorn#2481");
        var player2 = PlayerOverallStats.Create("ThunderHorn#21132");
        await playerRepository.UpsertPlayer(player);
        await playerRepository.UpsertPlayer(player2);
        var playerLoaded = await playerRepository.SearchForPlayer("thunder");

        Assert.AreEqual(player.BattleTag, playerLoaded[1].BattleTag);
        Assert.AreEqual(player2.BattleTag, playerLoaded[0].BattleTag);
        Assert.AreEqual(2, playerLoaded.Count);
    }

    [Test]
    public async Task Search_DuplicateNameBug_RefineWithBtag()
    {
        var playerRepository = new PlayerRepository(MongoClient);

        var player = PlayerOverallStats.Create("ThunderHorn#2481");
        var player2 = PlayerOverallStats.Create("ThunderHorn#21132");
        await playerRepository.UpsertPlayer(player);
        await playerRepository.UpsertPlayer(player2);
        var playerLoaded = await playerRepository.SearchForPlayer("thunderhorn#2481");

        Assert.AreEqual(player.BattleTag, playerLoaded[0].BattleTag);
        Assert.AreEqual(1, playerLoaded.Count);
    }

    [Test]
    public async Task GlobalSearch()
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
        var playerService = new PlayerService(playerRepository, CreateTestCache<List<MmrRank>>(), personalSettingsProvider);

        var player1 = new PersonalSetting("ThunderHorn#2481");
        var playerStats = PlayerOverallStats.Create("ThunderHorn#2481");
        playerStats.RecordWin(Race.HU, 1, true);
        player1.RaceWins = playerStats;
        SetPictureCommand cmd = new()
        {
            avatarCategory = AvatarCategory.HU,
            pictureId = 2
        };
        player1.SetProfilePicture(cmd);
        await personalSettingsRepository.Save(player1);
        await playerRepository.UpsertPlayer(playerStats);

        var player2 = new PersonalSetting("ThunderHorn#21132");
        await personalSettingsRepository.Save(player2);

        var player3 = new PersonalSetting("OtherPlayer#123");
        await personalSettingsRepository.Save(player3);

        var players = await playerService.GlobalSearchForPlayer("under");
        Assert.AreEqual(2, players.Count);
        Assert.AreEqual(player2.Id, players[0].BattleTag);
        Assert.AreEqual(player1.Id, players[1].BattleTag);

        players = await playerService.GlobalSearchForPlayer("under", "9_ThunderHorn#21132");
        Assert.AreEqual(1, players.Count);
        Assert.AreEqual(player1.Id, players[0].BattleTag);
        Assert.AreEqual(player1.ProfilePicture.PictureId, players[0].ProfilePicture.PictureId);
        Assert.AreEqual(1, players[0].Seasons.Count);
    }

    [Test]
    public async Task PlayerMapping()
    {
        var playerRepository = new PlayerRepository(MongoClient);

        var player = PlayerOverallStats.Create("peter#123");
        player.RecordWin(Race.HU, 0, true);
        await playerRepository.UpsertPlayer(player);
        var playerLoaded = await playerRepository.LoadPlayerOverallStats(player.BattleTag);
        playerLoaded.RecordWin(Race.UD, 0, false);
        await playerRepository.UpsertPlayer(playerLoaded);

        var playerLoadedAgain = await playerRepository.LoadPlayerOverallStats(player.BattleTag);

        Assert.AreEqual(player.BattleTag, playerLoaded.BattleTag);
        Assert.AreEqual(player.BattleTag, playerLoadedAgain.BattleTag);
    }

    [Test]
    public async Task PlayerStatsMapping()
    {
        var playerRepository = new PlayerRepository(MongoClient);

        var battleTagIdCombined = new BattleTagIdCombined(new List<PlayerId>
            {
                PlayerId.Create("peter#123")
            },
            GateWay.Europe,
            GameMode.GM_1v1,
            1,
            null);
        var player = PlayerGameModeStatPerGateway.Create(battleTagIdCombined);
        player.RecordRanking(234, 123);

        await playerRepository.UpsertPlayerGameModeStatPerGateway(player);

        var playerLoadedAgain = await playerRepository.LoadGameModeStatPerGateway("peter#123", GateWay.Europe, 1);

        Assert.AreEqual(234, playerLoadedAgain.Single().MMR);
        Assert.AreEqual(123, playerLoadedAgain.Single().RankingPoints);
    }

    [Test]
    public async Task PlayerIdMappedRight()
    {
        var playerRepository = new PlayerRepository(MongoClient);

        var player1 = PlayerOverallStats.Create("peter#123");
        var player2 = PlayerOverallStats.Create("wolf#456");

        await playerRepository.UpsertPlayer(player1);
        await playerRepository.UpsertPlayer(player2);

        var playerLoaded = await playerRepository.LoadPlayerOverallStats(player2.BattleTag);

        Assert.IsNotNull(playerLoaded);
        Assert.AreEqual(player2.BattleTag, playerLoaded.BattleTag);
    }

    [Test]
    public async Task PlayerMultipleWinRecords()
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var personalSettingsRepo = new PersonalSettingsRepository(MongoClient);

        var handler = new PlayerOverallStatsHandler(playerRepository, personalSettingsRepo);
        var handler2 = new PlayerGameModeStatPerGatewayHandler(playerRepository);
        var handler3 = new PlayerRaceStatPerGatewayHandler(playerRepository);

        var ev = TestDtoHelper.CreateFakeEvent();
        ev.match.gameMode = GameMode.GM_1v1;
        ev.match.gateway = GateWay.Europe;
        ev.match.season = 1;
        ev.match.players[0].battleTag = "peter#123";
        ev.match.players[0].battleTag = "peter#123";
        ev.match.players[0].race = Race.HU;
        ev.match.players[1].race = Race.OC;

        for (int i = 0; i < 5; i++)
        {
            await handler.Update(ev);
            await handler2.Update(ev);
            await handler3.Update(ev);
        }

        var playerLoaded = await playerRepository.LoadRaceStatPerGateway("peter#123", Race.HU, GateWay.Europe, 1);
        var playerLoadedStats = await playerRepository.LoadGameModeStatPerGateway("peter#123", GateWay.Europe, 1);

        Assert.AreEqual(5, playerLoadedStats.Single().Wins);
        Assert.AreEqual(5, playerLoaded.Wins);
    }


    [Test]
    public async Task PlayersFromAn1v1Match_GameModeHandler_Test()
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var handler = new PlayerGameModeStatPerGatewayHandler(playerRepository);

        var ev = TestDtoHelper.CreateFakeEvent();
        ev.match.season = 1;

        ev.match.players[0].battleTag = "peter#123";
        ev.match.players[0].won = true;

        ev.match.players[1].battleTag = "wolf#456";
        ev.match.players[1].won = false;

        await handler.Update(ev);

        var winnerStatGateWay = await playerRepository.LoadGameModeStatPerGateway("peter#123", GateWay.Europe, 1);
        var loser = await playerRepository.LoadGameModeStatPerGateway("wolf#456", GateWay.Europe, 1);

        Assert.AreEqual(1, winnerStatGateWay.First(x => x.GameMode == GameMode.GM_1v1).Wins);

        Assert.AreEqual(1, loser.First(x => x.GameMode == GameMode.GM_1v1).Losses);
        Assert.AreEqual(0, loser.First(x => x.GameMode == GameMode.GM_1v1).Wins);
    }

    [Test]
    public async Task PlayersFromAn2v2ATMatch_GameModeHandler_Test()
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var handler = new PlayerGameModeStatPerGatewayHandler(playerRepository);

        var ev = TestDtoHelper.CreateFake2v2AtEvent();
        ev.match.season = 1;

        ev.match.players[0].battleTag = "peter#123";
        ev.match.players[0].won = true;

        ev.match.players[1].battleTag = "wolf#456";
        ev.match.players[1].won = true;

        ev.match.players[2].battleTag = "TEAM2#123";
        ev.match.players[2].won = false;

        ev.match.players[3].battleTag = "TEAM2#456";
        ev.match.players[3].won = false;

        await handler.Update(ev);

        var winners = await playerRepository.LoadGameModeStatPerGateway("1_peter#123@10_wolf#456@10_GM_2v2_AT");
        var losers = await playerRepository.LoadGameModeStatPerGateway("1_TEAM2#123@10_TEAM2#456@10_GM_2v2_AT");

        Assert.AreEqual(1, winners.Wins);
        Assert.AreEqual(0, winners.Losses);

        Assert.AreEqual(1, losers.Losses);
        Assert.AreEqual(0, losers.Wins);
    }

    [Test]
    public async Task PlayersFromAn2v2AT_vs_RTMatch_GameModeHandler_Test()
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var handler = new PlayerGameModeStatPerGatewayHandler(playerRepository);

        var ev = TestDtoHelper.CreateFake2v2AtEvent();
        ev.match.season = 1;

        ev.match.players[0].battleTag = "peter#123";
        ev.match.players[0].won = true;
        ev.match.players[0].atTeamId = null;

        ev.match.players[1].battleTag = "wolf#456";
        ev.match.players[1].won = true;
        ev.match.players[1].atTeamId = null;

        ev.match.players[2].battleTag = "TEAM2#123";
        ev.match.players[2].won = false;

        ev.match.players[3].battleTag = "TEAM2#456";
        ev.match.players[3].won = false;

        await handler.Update(ev);

        var winnerP1 = await playerRepository.LoadGameModeStatPerGateway($"1_peter#123@10_GM_2v2");
        var winnerP2 = await playerRepository.LoadGameModeStatPerGateway($"1_wolf#456@10_GM_2v2");
        var losers = await playerRepository.LoadGameModeStatPerGateway("1_TEAM2#123@10_TEAM2#456@10_GM_2v2_AT");

        Assert.AreEqual(1, winnerP1.Wins);
        Assert.AreEqual(0, winnerP1.Losses);

        Assert.AreEqual(1, winnerP2.Wins);
        Assert.AreEqual(0, winnerP2.Losses);

        Assert.AreEqual(1, losers.Losses);
        Assert.AreEqual(0, losers.Wins);
    }

    [Test]
    public async Task PlayersFromAnFFAMatch_GameModeHandler_Test()
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var handler = new PlayerGameModeStatPerGatewayHandler(playerRepository);

        var ev = TestDtoHelper.CreateFakeFFAEvent();
        ev.match.season = 1;

        ev.match.players[0].battleTag = "peter#123";
        ev.match.players[0].won = true;

        ev.match.players[1].battleTag = "wolf#456";
        ev.match.players[1].won = false;

        ev.match.players[2].battleTag = "TEAM3#123";
        ev.match.players[2].won = false;

        ev.match.players[3].battleTag = "TEAM4#456";
        ev.match.players[3].won = false;

        await handler.Update(ev);

        var winnerStatGateWay = await playerRepository.LoadGameModeStatPerGateway("peter#123", GateWay.Europe, 1);
        var loser1 = await playerRepository.LoadGameModeStatPerGateway("wolf#456", GateWay.Europe, 1);
        var loser2 = await playerRepository.LoadGameModeStatPerGateway("TEAM3#123", GateWay.Europe, 1);
        var loser3 = await playerRepository.LoadGameModeStatPerGateway("TEAM4#456", GateWay.Europe, 1);

        Assert.AreEqual(1, winnerStatGateWay.First(x => x.GameMode == GameMode.FFA).Wins);

        Assert.AreEqual(1, loser1.First(x => x.GameMode == GameMode.FFA).Losses);
        Assert.AreEqual(0, loser1.First(x => x.GameMode == GameMode.FFA).Wins);

        Assert.AreEqual(1, loser2.First(x => x.GameMode == GameMode.FFA).Losses);
        Assert.AreEqual(0, loser2.First(x => x.GameMode == GameMode.FFA).Wins);

        Assert.AreEqual(1, loser3.First(x => x.GameMode == GameMode.FFA).Losses);
        Assert.AreEqual(0, loser3.First(x => x.GameMode == GameMode.FFA).Wins);
    }

    [Test]
    public async Task UpdateOverview_HandlerUpdate_FootmenFrenzy()
    {
        var matchFinishedEvent = TestDtoHelper.CreateFakeFootmenFrenzyEvent();

        var playerRepository = new PlayerRepository(MongoClient);
        var handler = new PlayerGameModeStatPerGatewayHandler(playerRepository);

        await handler.Update(matchFinishedEvent);

        var winners = matchFinishedEvent.match.players.Where(x => x.won);

        Assert.AreEqual(3, winners.Count());

        foreach (var player in winners)
        {
            var winnerStatGateWay = await playerRepository.LoadGameModeStatPerGateway(player.battleTag, GateWay.Europe, matchFinishedEvent.match.season);

            Assert.AreEqual(1, winnerStatGateWay.First(x => x.GameMode == GameMode.GM_FOOTMEN_FRENZY).Wins);
            Assert.AreEqual(0, winnerStatGateWay.First(x => x.GameMode == GameMode.GM_FOOTMEN_FRENZY).Losses);
        }

        var losers = matchFinishedEvent.match.players.Where(x => !x.won);

        Assert.AreEqual(9, losers.Count());

        foreach (var player in losers)
        {
            var winnerStatGateWay = await playerRepository.LoadGameModeStatPerGateway(player.battleTag, GateWay.Europe, matchFinishedEvent.match.season);

            Assert.AreEqual(0, winnerStatGateWay.First(x => x.GameMode == GameMode.GM_FOOTMEN_FRENZY).Wins);
            Assert.AreEqual(1, winnerStatGateWay.First(x => x.GameMode == GameMode.GM_FOOTMEN_FRENZY).Losses);
        }
    }

    [Test]
    public async Task Player_UpdateMmrRpTimeline()
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var handler = new PlayerMmrRpTimelineHandler(playerRepository);
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent3 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent4 = TestDtoHelper.CreateFakeEvent();

        // P1 Win
        matchFinishedEvent1.match.endTime = 1585692047363;
        matchFinishedEvent1.match.players[0].race = Race.OC;
        matchFinishedEvent1.match.players[1].race = Race.NE;
        matchFinishedEvent1.match.players[0].updatedMmr.rating = 123;
        matchFinishedEvent1.match.players[1].updatedMmr.rating = 77;
        matchFinishedEvent1.match.players.ForEach(x => x.atTeamId = null);


        // P1 Lose
        matchFinishedEvent2.match.endTime = 1585701559200;
        matchFinishedEvent2.match.players[0].won = false;
        matchFinishedEvent2.match.players[1].won = true;
        matchFinishedEvent2.match.players[0].race = Race.OC;
        matchFinishedEvent2.match.players[1].race = Race.NE;
        matchFinishedEvent2.match.players[0].updatedMmr.rating = 98;
        matchFinishedEvent2.match.players[1].updatedMmr.rating = 102;
        matchFinishedEvent2.match.players.ForEach(x => x.atTeamId = null);

        // matchFinishedEvent2 and 3 on the same day, so only the one with later date should appear in MmrRpTimeline

        // P1 Lose
        matchFinishedEvent3.match.endTime = 1585702659200;
        matchFinishedEvent3.match.players[0].won = false;
        matchFinishedEvent3.match.players[1].won = true;
        matchFinishedEvent3.match.players[0].race = Race.OC;
        matchFinishedEvent3.match.players[1].race = Race.NE;
        matchFinishedEvent3.match.players[0].updatedMmr.rating = 80;
        matchFinishedEvent3.match.players[1].updatedMmr.rating = 120;
        matchFinishedEvent3.match.players.ForEach(x => x.atTeamId = null);

        // P1 Win
        matchFinishedEvent4.match.endTime = 1604612998269;
        matchFinishedEvent4.match.players[0].race = Race.OC;
        matchFinishedEvent4.match.players[1].race = Race.NE;
        matchFinishedEvent4.match.players[0].updatedMmr.rating = 102;
        matchFinishedEvent4.match.players[1].updatedMmr.rating = 98;
        matchFinishedEvent4.match.players.ForEach(x => x.atTeamId = null);

        // Wrong order on purpose!
        await handler.Update(matchFinishedEvent4);
        await handler.Update(matchFinishedEvent2);
        await handler.Update(matchFinishedEvent1);
        await handler.Update(matchFinishedEvent3);


        var peterMmrRpTimeline = await playerRepository.LoadPlayerMmrRpTimeline("peter#123", Race.OC, GateWay.Europe, 0, GameMode.GM_1v1);
        var wolfMmrRpTimeline = await playerRepository.LoadPlayerMmrRpTimeline("wolf#456", Race.NE, GateWay.Europe, 0, GameMode.GM_1v1);

        Assert.IsNotNull(peterMmrRpTimeline);
        Assert.IsNotNull(wolfMmrRpTimeline);

        Assert.IsTrue(peterMmrRpTimeline.MmrRpAtDates[0].Mmr > peterMmrRpTimeline.MmrRpAtDates[1].Mmr);
        Assert.IsTrue(peterMmrRpTimeline.MmrRpAtDates[1].Mmr < peterMmrRpTimeline.MmrRpAtDates[2].Mmr);

        Assert.IsTrue(wolfMmrRpTimeline.MmrRpAtDates[0].Mmr < wolfMmrRpTimeline.MmrRpAtDates[1].Mmr);
        Assert.IsTrue(wolfMmrRpTimeline.MmrRpAtDates[1].Mmr > wolfMmrRpTimeline.MmrRpAtDates[2].Mmr);
    }

    [Test]
    [TestCase(false, TestName = "Player_UpdateMmrRpTimeline_MergesSameDayEntries")]
    [TestCase(true, TestName = "Player_UpdateMmrRpTimeline_MergesSameDayEntries_OutOfOrder")]
    public async Task Player_UpdateMmrRpTimeline_MergesSameDayEntries(bool outOfOrder)
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var handler = new PlayerMmrRpTimelineHandler(playerRepository);

        // Three games on one day: a climb to 150, then a slide back down to 80.
        // The day should keep 80 as its closing rating but still remember 150.
        // rd stays above the obfuscation threshold throughout, so it is stored
        // and the day's closing value can be asserted.
        var events = new[] { (1585692047363L, 120, 400.0), (1585695047363L, 150, 320.0), (1585698047363L, 80, 260.0) }
            .Select(x =>
            {
                var ev = TestDtoHelper.CreateFakeEvent();
                ev.match.endTime = x.Item1;
                ev.match.players[0].race = Race.OC;
                ev.match.players[1].race = Race.NE;
                ev.match.players[0].updatedMmr.rating = x.Item2;
                ev.match.players[0].updatedMmr.rd = x.Item3;
                ev.match.players.ForEach(p => p.atTeamId = null);
                return ev;
            })
            .ToList();

        foreach (var ev in outOfOrder ? new[] { events[2], events[0], events[1] } : events.ToArray())
        {
            await handler.Update(ev);
        }

        var timeline = await playerRepository.LoadPlayerMmrRpTimeline("peter#123", Race.OC, GateWay.Europe, 0, GameMode.GM_1v1);

        Assert.IsNotNull(timeline);
        Assert.AreEqual(1, timeline.MmrRpAtDates.Count, "three games on one day should collapse to a single entry");

        var day = timeline.MmrRpAtDates[0];
        Assert.AreEqual(80, day.Mmr, "the day closes on the last game played");
        Assert.AreEqual(150, day.DailyMaxMmr, "the intra-day peak survives the later losses");
        Assert.AreEqual(3, day.Games);
        Assert.AreEqual(260.0, day.Rd, "rd comes from the last game of the day");
        Assert.AreEqual(PlayerMmrRpTimeline.CurrentSchemaVersion, timeline.SchemaVersion);
    }

    [Test]
    public async Task Player_UpdateMmrRpTimeline_IsIdempotentWhenAnEventIsReplayed()
    {
        // The read-model pipeline delivers at least once: a handler that throws part
        // way through a match leaves the players it already wrote committed and the
        // event is retried every few seconds, and a crash between the handler
        // succeeding and the watermark saving replays it once on restart. The games
        // count accumulates, so without a guard each replay would add another game.
        var playerRepository = new PlayerRepository(MongoClient);
        var handler = new PlayerMmrRpTimelineHandler(playerRepository);

        var ev = TestDtoHelper.CreateFakeEvent();
        ev.match.endTime = 1585692047363L;
        ev.match.players[0].race = Race.OC;
        ev.match.players[1].race = Race.NE;
        ev.match.players[0].updatedMmr.rating = 120;
        ev.match.players.ForEach(p => p.atTeamId = null);

        await handler.Update(ev);
        await handler.Update(ev);
        await handler.Update(ev);

        var timeline = await playerRepository.LoadPlayerMmrRpTimeline("peter#123", Race.OC, GateWay.Europe, 0, GameMode.GM_1v1);

        Assert.IsNotNull(timeline);
        Assert.AreEqual(1, timeline.MmrRpAtDates.Count);
        var day = timeline.MmrRpAtDates[0];
        Assert.IsNull(day.Games, "a single game stays at the omitted default rather than counting the replays");
        Assert.AreEqual(1, day.GamesOrDefault(timeline.SchemaVersion));
        Assert.AreEqual(120, day.Mmr);
    }

    [Test]
    public async Task Player_UpdateMmrRpTimeline_ReplayingTheLatestEventDoesNotInflateTheDay()
    {
        // The same invariant on a day that legitimately has more than one game:
        // replaying only the most recent event must leave the count at two, not three.
        var playerRepository = new PlayerRepository(MongoClient);
        var handler = new PlayerMmrRpTimelineHandler(playerRepository);

        var events = new[] { (1585692047363L, 120), (1585695047363L, 150) }
            .Select(x =>
            {
                var ev = TestDtoHelper.CreateFakeEvent();
                ev.match.endTime = x.Item1;
                ev.match.players[0].race = Race.OC;
                ev.match.players[1].race = Race.NE;
                ev.match.players[0].updatedMmr.rating = x.Item2;
                ev.match.players.ForEach(p => p.atTeamId = null);
                return ev;
            })
            .ToList();

        await handler.Update(events[0]);
        await handler.Update(events[1]);
        await handler.Update(events[1]);

        var timeline = await playerRepository.LoadPlayerMmrRpTimeline("peter#123", Race.OC, GateWay.Europe, 0, GameMode.GM_1v1);

        Assert.AreEqual(1, timeline.MmrRpAtDates.Count);
        Assert.AreEqual(2, timeline.MmrRpAtDates[0].Games, "the replayed match is not counted twice");
        Assert.AreEqual(150, timeline.MmrRpAtDates[0].Mmr);
    }

    [Test]
    public async Task Player_UpdateMmrRpTimeline_OmitsRedundantFields()
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var handler = new PlayerMmrRpTimelineHandler(playerRepository);

        // A single game by a settled player: one game, close is the peak, and an
        // rd well under the storage floor. None of that needs storing.
        var settled = TestDtoHelper.CreateFakeEvent();
        settled.match.endTime = 1585692047363;
        settled.match.players[0].race = Race.OC;
        settled.match.players[1].race = Race.NE;
        settled.match.players[0].updatedMmr.rating = 1500;
        settled.match.players[0].updatedMmr.rd = 80;
        settled.match.players.ForEach(p => p.atTeamId = null);
        await handler.Update(settled);

        var timeline = await playerRepository.LoadPlayerMmrRpTimeline("peter#123", Race.OC, GateWay.Europe, 0, GameMode.GM_1v1);
        var day = timeline.MmrRpAtDates[0];

        Assert.IsNull(day.Games, "a single game is what absence already means");
        Assert.IsNull(day.DailyMaxMmr, "peak equals close, so it carries nothing");
        Assert.IsNull(day.Rd, "a settled rd says nothing about calibration");

        // Absence still reads back as the intended value.
        Assert.AreEqual(1, day.GamesOrDefault(timeline.SchemaVersion));
        Assert.AreEqual(1500, day.PeakMmr);
        Assert.IsFalse(day.WasCalibrating);
    }

    [Test]
    public async Task Player_UpdateMmrRpTimeline_KeepsRdWhileCalibrating()
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var handler = new PlayerMmrRpTimelineHandler(playerRepository);

        // A player's first game still carries the 1v1 starting rd of 500.
        var placement = TestDtoHelper.CreateFakeEvent();
        placement.match.endTime = 1585692047363;
        placement.match.players[0].race = Race.OC;
        placement.match.players[1].race = Race.NE;
        placement.match.players[0].updatedMmr.rating = 1500;
        placement.match.players[0].updatedMmr.rd = 500;
        placement.match.players.ForEach(p => p.atTeamId = null);
        await handler.Update(placement);

        var timeline = await playerRepository.LoadPlayerMmrRpTimeline("peter#123", Race.OC, GateWay.Europe, 0, GameMode.GM_1v1);
        var day = timeline.MmrRpAtDates[0];

        Assert.AreEqual(500, day.Rd);
        Assert.IsTrue(day.WasCalibrating,
            "without this the 1500 placement rating would count as a lifetime peak");
    }

    // A settled rating sits at 80 in most modes, 110 in Risk Europe and 135 in
    // Direct Strike, all comfortably under the 240 line the match API already
    // uses, so one mode-independent threshold covers every mode.
    [TestCase(GameMode.GM_1v1, 80)]
    [TestCase(GameMode.GM_DS, 135)]
    [TestCase(GameMode.GM_RISK_EUROPE, 110)]
    public async Task Player_UpdateMmrRpTimeline_SettledRdIsNotStored(GameMode gameMode, double settledRd)
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var handler = new PlayerMmrRpTimelineHandler(playerRepository);

        var ev = TestDtoHelper.CreateFakeEvent();
        ev.match.gameMode = gameMode;
        ev.match.endTime = 1585692047363;
        ev.match.players[0].race = Race.OC;
        ev.match.players[1].race = Race.NE;
        ev.match.players[0].updatedMmr.rating = 1500;
        ev.match.players[0].updatedMmr.rd = settledRd;
        ev.match.players.ForEach(p => p.atTeamId = null);
        await handler.Update(ev);

        var timeline = await playerRepository.LoadPlayerMmrRpTimeline("peter#123", Race.OC, GateWay.Europe, 0, gameMode);

        Assert.IsNull(timeline.MmrRpAtDates[0].Rd, $"{settledRd} is settled for {gameMode}");
        Assert.IsFalse(timeline.MmrRpAtDates[0].WasCalibrating);
    }

    [Test]
    public async Task Player_UpdateMmrRpTimeline_4v4_WithArrangedTeams()
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var handler = new PlayerMmrRpTimelineHandler(playerRepository);
        var matchFinishedEvent = TestDtoHelper.CreateFake4v4Event();

        // Setup 4v4 match with 2 AT players per team and 2 RT players per team
        matchFinishedEvent.match.endTime = 1585692047363;
        matchFinishedEvent.match.gameMode = GameMode.GM_4v4;

        // Team 0 (Winners) - 2 AT players, 2 RT players
        matchFinishedEvent.match.players[0].battleTag = "at1#123";
        matchFinishedEvent.match.players[0].atTeamId = "team1";
        matchFinishedEvent.match.players[0].race = Race.HU;
        matchFinishedEvent.match.players[0].updatedMmr.rating = 1500;
        matchFinishedEvent.match.players[0].won = true;

        matchFinishedEvent.match.players[1].battleTag = "at2#456";
        matchFinishedEvent.match.players[1].atTeamId = "team1";
        matchFinishedEvent.match.players[1].race = Race.OC;
        matchFinishedEvent.match.players[1].updatedMmr.rating = 1450;
        matchFinishedEvent.match.players[1].won = true;

        matchFinishedEvent.match.players[2].battleTag = "rt1#789";
        matchFinishedEvent.match.players[2].atTeamId = null;
        matchFinishedEvent.match.players[2].race = Race.UD;
        matchFinishedEvent.match.players[2].updatedMmr.rating = 1600;
        matchFinishedEvent.match.players[2].won = true;

        matchFinishedEvent.match.players[3].battleTag = "rt2#101";
        matchFinishedEvent.match.players[3].atTeamId = null;
        matchFinishedEvent.match.players[3].race = Race.NE;
        matchFinishedEvent.match.players[3].updatedMmr.rating = 1550;
        matchFinishedEvent.match.players[3].won = true;

        // Team 1 (Losers) - 2 AT players, 2 RT players
        matchFinishedEvent.match.players[4].battleTag = "at3#234";
        matchFinishedEvent.match.players[4].atTeamId = "team2";
        matchFinishedEvent.match.players[4].race = Race.HU;
        matchFinishedEvent.match.players[4].updatedMmr.rating = 1400;
        matchFinishedEvent.match.players[4].won = false;

        matchFinishedEvent.match.players[5].battleTag = "at4#567";
        matchFinishedEvent.match.players[5].atTeamId = "team2";
        matchFinishedEvent.match.players[5].race = Race.OC;
        matchFinishedEvent.match.players[5].updatedMmr.rating = 1350;
        matchFinishedEvent.match.players[5].won = false;

        matchFinishedEvent.match.players[6].battleTag = "rt3#890";
        matchFinishedEvent.match.players[6].atTeamId = null;
        matchFinishedEvent.match.players[6].race = Race.UD;
        matchFinishedEvent.match.players[6].updatedMmr.rating = 1300;
        matchFinishedEvent.match.players[6].won = false;

        matchFinishedEvent.match.players[7].battleTag = "rt4#112";
        matchFinishedEvent.match.players[7].atTeamId = null;
        matchFinishedEvent.match.players[7].race = Race.NE;
        matchFinishedEvent.match.players[7].updatedMmr.rating = 1250;
        matchFinishedEvent.match.players[7].won = false;

        // AT players should cause exceptions/skips, RT players should be processed
        await handler.Update(matchFinishedEvent);

        // Verify RT players have MMR timeline updates
        var rt1Timeline = await playerRepository.LoadPlayerMmrRpTimeline("rt1#789", Race.UD, GateWay.Europe, 0, GameMode.GM_4v4);
        var rt2Timeline = await playerRepository.LoadPlayerMmrRpTimeline("rt2#101", Race.NE, GateWay.Europe, 0, GameMode.GM_4v4);
        var rt3Timeline = await playerRepository.LoadPlayerMmrRpTimeline("rt3#890", Race.UD, GateWay.Europe, 0, GameMode.GM_4v4);
        var rt4Timeline = await playerRepository.LoadPlayerMmrRpTimeline("rt4#112", Race.NE, GateWay.Europe, 0, GameMode.GM_4v4);

        Assert.IsNotNull(rt1Timeline);
        Assert.IsNotNull(rt2Timeline);
        Assert.IsNotNull(rt3Timeline);
        Assert.IsNotNull(rt4Timeline);

        // Verify AT players do NOT have MMR timeline updates
        var at1Timeline = await playerRepository.LoadPlayerMmrRpTimeline("at1#123", Race.HU, GateWay.Europe, 0, GameMode.GM_4v4);
        var at2Timeline = await playerRepository.LoadPlayerMmrRpTimeline("at2#456", Race.OC, GateWay.Europe, 0, GameMode.GM_4v4);
        var at3Timeline = await playerRepository.LoadPlayerMmrRpTimeline("at3#234", Race.HU, GateWay.Europe, 0, GameMode.GM_4v4);
        var at4Timeline = await playerRepository.LoadPlayerMmrRpTimeline("at4#567", Race.OC, GateWay.Europe, 0, GameMode.GM_4v4);

        Assert.IsNull(at1Timeline);
        Assert.IsNull(at2Timeline);
        Assert.IsNull(at3Timeline);
        Assert.IsNull(at4Timeline);
    }

    [Test]
    [TestCase("player2#456", Race.OC, 0.666666f, Description = "Middle player - 66% quantile")]
    [TestCase("top#123", Race.HU, 1.0f, Description = "Top player - 100% quantile")]
    [TestCase("bottom#789", Race.NE, 0.333333f, Description = "Bottom player - 33% quantile")]
    [TestCase("nonexistent#999", Race.OC, null, Description = "Non-existent player - returns null")]
    public async Task GetQuantileForPlayer_CalculatesCorrectly(string battleTag, Race race, float? expectedQuantile)
    {
        // Arrange
        var playerRepository = new PlayerRepository(MongoClient);
        var playerService = new PlayerService(playerRepository, CreateTestCache<List<MmrRank>>(), personalSettingsProvider);

        // Setup test data - 3 players with different MMRs
        var testPlayers = new[]
        {
            ("top#123", 2000, Race.HU),
            ("player2#456", 1500, Race.OC),
            ("bottom#789", 1000, Race.NE)
        };

        foreach (var (tag, mmr, r) in testPlayers)
        {
            var overview = PlayerOverview.Create(
                new List<PlayerId> { PlayerId.Create(tag) },
                GateWay.Europe,
                GameMode.GM_1v1,
                1,
                r
            );
            overview.MMR = mmr;
            await playerRepository.UpsertPlayerOverview(overview);
        }

        // Act
        var quantile = await playerService.GetQuantileForPlayer(
            new List<PlayerId> { PlayerId.Create(battleTag) },
            GateWay.Europe,
            GameMode.GM_1v1,
            race,
            1
        );

        // Assert
        if (expectedQuantile.HasValue)
            Assert.That(quantile, Is.EqualTo(expectedQuantile.Value).Within(1e-6f));
        else
            Assert.IsNull(quantile);
    }
}
