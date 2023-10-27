using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;
using W3ChampionsStatisticService.PlayerStats;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace WC3ChampionsStatisticService.Tests.Statistics;

[TestFixture]
public class PlayerStatsTests : IntegrationTestBase
{
    [Test]
    public async Task LoadAndSaveMapAndRace()
    {
        var playerRepository = new PlayerStatsRepository(MongoClient);

        var player = PlayerRaceOnMapVersusRaceRatio.Create("peter#123", 0);
        await playerRepository.UpsertMapAndRaceStat(player);
        var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.BattleTag, 0);

        Assert.AreEqual(player.BattleTag, playerLoaded.BattleTag);
    }

    [Test]
    public async Task MapWinsAndRace()
    {
        var playerRepository = new PlayerStatsRepository(MongoClient);

        var player = PlayerRaceOnMapVersusRaceRatio.Create("peter#123", 0);
        var patch = "1.32.5";
        player.AddMapWin(Race.HU, Race.UD, "TM", true, patch);
        player.AddMapWin(Race.HU, Race.OC, "EI", true, patch);
        player.AddMapWin(Race.HU, Race.UD, "TM", false, patch);

        await playerRepository.UpsertMapAndRaceStat(player);
        var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.BattleTag, 0);

        Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, Race.UD, "TM", patch).Wins);
        Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, Race.UD, "TM", patch).Losses);
        Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, Race.OC, "EI", patch).Wins);
    }

    [Test]
    public async Task MapWinsAndRaceRnd()
    {
        var playerRepository = new PlayerStatsRepository(MongoClient);

        var player = PlayerRaceOnMapVersusRaceRatio.Create("peter#123", 0);
        var patch = "1.32.5";

        player.AddMapWin(Race.RnD, Race.UD, "TM", true, patch);
        player.AddMapWin(Race.HU, Race.RnD, "EI", false, patch);

        await playerRepository.UpsertMapAndRaceStat(player);
        var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.BattleTag, 0);

        Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.RnD, Race.UD, "TM", patch).Wins);
        Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, Race.RnD, "EI", patch).Losses);
    }

    [Test]
    public async Task MapWinsAsTotalRace()
    {
        var playerRepository = new PlayerStatsRepository(MongoClient);

        var player = PlayerRaceOnMapVersusRaceRatio.Create("peter#123", 0);
        var patch = "1.32.5";
        player.AddMapWin(Race.HU, Race.UD, "TM", true, patch);
        player.AddMapWin(Race.NE, Race.UD, "TM", true, patch);
        player.AddMapWin(Race.OC, Race.UD, "TM", true, patch);

        await playerRepository.UpsertMapAndRaceStat(player);
        var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.BattleTag, 0);

        Assert.AreEqual(3, playerLoaded.GetWinLoss(Race.Total, Race.UD, "TM", patch).Wins);
    }

    [Test]
    public async Task PlayerHeroStats_PlayerAgainstTwoEnemies_PlayerShouldHaveCorrectHeroStats()
    {
        var playerRepository = new PlayerStatsRepository(MongoClient);
        var playerHeroStatsHandler = new PlayerHeroStatsHandler(playerRepository);

        var player = CreatePlayer("player#123", Race.HU, won: true);
        var playerHeroes = CreateHeroes("archmage", "mountainking");

        var enemyUdPlayer = CreatePlayer("enemyUd#567", Race.UD);
        var enemyUdHeroes = CreateHeroes("deathknight", "lich");

        // Match 1 Hu vs UD
        MatchFinishedEvent match1 = CreateMatchEvent(player, playerHeroes, enemyUdPlayer, enemyUdHeroes);
        await playerHeroStatsHandler.Update(match1);

        // Match 2 Hu vs NE
        var enemyNePlayer = CreatePlayer("enemyNe#89", Race.NE);
        var enemyNeHeroes = CreateHeroes("deamonhunter");
        MatchFinishedEvent match2 = CreateMatchEvent(player, playerHeroes, enemyNePlayer, enemyNeHeroes);
        await playerHeroStatsHandler.Update(match2);

        var playerHeroStats = await playerRepository.LoadHeroStat(player.battleTag, 0);
        var enemyUdHeroStats = await playerRepository.LoadHeroStat(enemyUdPlayer.battleTag, 0);
        var enemyNeHeroStats = await playerRepository.LoadHeroStat(enemyNePlayer.battleTag, 0);

        // *** Player hero stats
        Assert.AreEqual(player.battleTag, playerHeroStats.BattleTag);
        Assert.AreEqual(2, playerHeroStats.HeroStatsItemList.Count);

        // Archmage Stats
        var playerAmStats = GetHeroStatsForRaceAndMap(playerHeroStats, "archmage", Race.HU, "Overall");

        var playerAmVsUd = playerAmStats.WinLosses.Single(x => x.Race == Race.UD);
        Assert.AreEqual(1, playerAmVsUd.Wins);
        Assert.AreEqual(0, playerAmVsUd.Losses);

        var playerAmVsNe = playerAmStats.WinLosses.Single(x => x.Race == Race.UD);
        Assert.AreEqual(1, playerAmVsNe.Wins);
        Assert.AreEqual(0, playerAmVsNe.Losses);

        // Mountain king Stats
        var playerMKStats = GetHeroStatsForRaceAndMap(playerHeroStats, "mountainking", Race.HU, "Overall");

        var playerMKVsUd = playerMKStats.WinLosses.Single(x => x.Race == Race.UD);
        Assert.AreEqual(1, playerMKVsUd.Wins);
        Assert.AreEqual(0, playerMKVsUd.Losses);

        var playerMKVsNe = playerMKStats.WinLosses.Single(x => x.Race == Race.UD);
        Assert.AreEqual(1, playerMKVsNe.Wins);
        Assert.AreEqual(0, playerMKVsNe.Losses);
    }

    [TestCase(Race.HU)]
    [TestCase(Race.NE)]
    [TestCase(Race.OC)]
    [TestCase(Race.RnD)]
    [TestCase(Race.UD)]
    public async Task PlayerHeroStats_PlayerAgainstEnemyAndPlayerWins_EnemyShouldHaveCorrectHeroStats(Race enemyRace)
    {
        var playerRepository = new PlayerStatsRepository(MongoClient);
        var playerHeroStatsHandler = new PlayerHeroStatsHandler(playerRepository);

        var player = CreatePlayer("player#123", Race.HU, won: true);
        var playerHeroes = CreateHeroes("playerhero");

        var enemyPlayer = CreatePlayer("enemy#567", enemyRace);
        var enemyUdHeroes = CreateHeroes("enemyhero");

        MatchFinishedEvent match1 = CreateMatchEvent(player, playerHeroes, enemyPlayer, enemyUdHeroes);
        await playerHeroStatsHandler.Update(match1);

        var enemyHerosStats = await playerRepository.LoadHeroStat(enemyPlayer.battleTag, 0);

        Assert.AreEqual(enemyPlayer.battleTag, enemyHerosStats.BattleTag);
        Assert.AreEqual(1, enemyHerosStats.HeroStatsItemList.Count);

        var enemyHeroStats = GetHeroStatsForRaceAndMap(enemyHerosStats, "enemyhero", enemyRace, "Overall");

        var enemyHeroStatsVsPlayerRace = enemyHeroStats.WinLosses.Single(x => x.Race == Race.HU);
        Assert.AreEqual(0, enemyHeroStatsVsPlayerRace.Wins);
        Assert.AreEqual(1, enemyHeroStatsVsPlayerRace.Losses);
    }

    [TestCase(Race.HU)]
    [TestCase(Race.NE)]
    [TestCase(Race.OC)]
    [TestCase(Race.RnD)]
    [TestCase(Race.UD)]
    public async Task PlayerHeroStats_PlayerAgainstEnemyAndPlayerLosses_EnemyShouldHaveCorrectHeroStats(Race enemyRace)
    {
        var playerRepository = new PlayerStatsRepository(MongoClient);
        var playerHeroStatsHandler = new PlayerHeroStatsHandler(playerRepository);

        var player = CreatePlayer("player#123", Race.HU, won: false);
        var playerHeroes = CreateHeroes("playerhero");

        var enemyPlayer = CreatePlayer("enemy#567", enemyRace, won: true);
        var enemyUdHeroes = CreateHeroes("enemyhero");

        MatchFinishedEvent match1 = CreateMatchEvent(player, playerHeroes, enemyPlayer, enemyUdHeroes);
        await playerHeroStatsHandler.Update(match1);

        var enemyHerosStats = await playerRepository.LoadHeroStat(enemyPlayer.battleTag, 0);

        Assert.AreEqual(enemyPlayer.battleTag, enemyHerosStats.BattleTag);
        Assert.AreEqual(1, enemyHerosStats.HeroStatsItemList.Count);

        var enemyHeroStats = GetHeroStatsForRaceAndMap(enemyHerosStats, "enemyhero", enemyRace, "Overall");

        var enemyStatsVsPlayerRace = enemyHeroStats.WinLosses.Single(x => x.Race == Race.HU);
        Assert.AreEqual(1, enemyStatsVsPlayerRace.Wins);
        Assert.AreEqual(0, enemyStatsVsPlayerRace.Losses);
    }

    [Test]
    public async Task PlayerStats_PlayerParticipatedRaceIsCorrect()
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var personalSettingsRepo = new PersonalSettingsRepository(MongoClient);

        var playerHeroStatsHandler = new PlayerOverallStatsHandler(playerRepository, personalSettingsRepo);

        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent1.match.season = 0;
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent2.match.season = 1;
        var matchFinishedEvent3 = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent3.match.season = 1;

        await playerHeroStatsHandler.Update(matchFinishedEvent1);
        await playerHeroStatsHandler.Update(matchFinishedEvent2);
        await playerHeroStatsHandler.Update(matchFinishedEvent3);

        var enemyStatsVsPlayerRace = await playerRepository.LoadPlayerProfile(matchFinishedEvent1.match.players[0].battleTag);

        Assert.AreEqual(2, enemyStatsVsPlayerRace.ParticipatedInSeasons.Count);
        Assert.AreEqual(1, enemyStatsVsPlayerRace.ParticipatedInSeasons[0].Id);
        Assert.AreEqual(0, enemyStatsVsPlayerRace.ParticipatedInSeasons[1].Id);
    }

    [Test]
    public async Task PlayerStats_RaceBasedMMR()
    {
        var playerRepository = new PlayerRepository(MongoClient);
        var playerHeroStatsHandler = new PlayOverviewHandler(playerRepository);

        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();

        matchFinishedEvent1.match.gateway = GateWay.Europe;
        matchFinishedEvent2.match.gateway = GateWay.Europe;

        matchFinishedEvent1.match.gameMode = GameMode.GM_1v1;
        matchFinishedEvent2.match.gameMode = GameMode.GM_1v1;

        matchFinishedEvent1.match.season = 2;
        matchFinishedEvent2.match.season = 2;

        matchFinishedEvent1.match.players[0].race = Race.NE;
        matchFinishedEvent2.match.players[0].race = Race.NE;

        matchFinishedEvent1.match.players[0].battleTag = "peter#123";
        matchFinishedEvent2.match.players[0].battleTag = "peter#123";

        await playerHeroStatsHandler.Update(matchFinishedEvent1);
        await playerHeroStatsHandler.Update(matchFinishedEvent2);
        var enemyStatsVsPlayerRace = await playerRepository.LoadOverview("2_peter#123@20_GM_1v1_NE");

        Assert.AreEqual(2, enemyStatsVsPlayerRace.Wins);
        Assert.AreEqual(0, enemyStatsVsPlayerRace.Losses);
    }

    private static WinLossesPerMap GetHeroStatsForRaceAndMap(PlayerHeroStats playerHeroStats, string heroId, Race race, string mapName)
    {
        var heroStats = playerHeroStats.HeroStatsItemList.Single(x => x.HeroId == heroId).Stats;
        var heroRaceStats = heroStats.Single(x => x.Race == race);
        var heroRaceStatsOnMap = heroRaceStats.WinLossesOnMap.Single(x => x.Map == mapName);

        return heroRaceStatsOnMap;
    }

    private static MatchFinishedEvent CreateMatchEvent(
        PlayerMMrChange player,
        List<Hero> playerHeroes,
        PlayerMMrChange enemyPlayer,
        List<Hero> enemyHeroes)
    {
        var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();

        matchFinishedEvent.match.players[0] = player;
        matchFinishedEvent.result.players[0].heroes = playerHeroes;

        matchFinishedEvent.match.players[1] = enemyPlayer;
        matchFinishedEvent.result.players[1].heroes = enemyHeroes;

        return matchFinishedEvent;
    }

    private static List<Hero> CreateHeroes(params string[] heroes)
    {
        return heroes
            .Select(x => new Hero() { icon = x })
            .ToList();
    }

    private static PlayerMMrChange CreatePlayer(string playerId, Race race, bool won = false)
    {
        return new PlayerMMrChange
        {
            battleTag = playerId,
            race = race,
            won = won
        };
    }
}
