using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Mongo2Go;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Services;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.GameModeStats;

[TestFixture]
public class GameModeStatQueryHandlerTests
{
    private const string BattleTag = "hero#123";
    private const GateWay Gateway = GateWay.Europe;
    private const int Season = 2;

    private MongoDbRunner _runner;
    private MongoClient _mongoClient;
    private PlayerRepository _playerRepository;
    private ProgressionMilestoneRepository _milestoneRepository;
    private GameModeStatQueryHandler _handler;

    [SetUp]
    public void Setup()
    {
        _runner = MongoDbRunner.Start();
        _mongoClient = new MongoClient(_runner.ConnectionString);
        _playerRepository = new PlayerRepository(_mongoClient);
        _milestoneRepository = new ProgressionMilestoneRepository(_mongoClient);

        var personalSettingsProvider = new PersonalSettingsProvider(_mongoClient, CreateCache<List<PersonalSetting>>());
        var playerService = new PlayerService(_playerRepository, CreateCache<List<MmrRank>>(), personalSettingsProvider);
        var rankRepository = new RankRepository(_mongoClient, personalSettingsProvider);
        var progressionViewLoader = new ProgressionViewLoader(new PlayerProgressionRepository(_mongoClient));
        var milestoneViewLoader = new MilestoneViewLoader(_milestoneRepository);

        _handler = new GameModeStatQueryHandler(
            _playerRepository,
            playerService,
            Mock.Of<ITrackingService>(),
            rankRepository,
            progressionViewLoader,
            milestoneViewLoader);
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    private static ICachedDataProvider<T> CreateCache<T>() where T : class =>
        new InMemoryCachedDataProvider<T>(
            new OptionsWrapper<CacheOptionsFor<T>>(new CacheOptionsFor<T>()),
            new MemoryCache(new MemoryCacheOptions()));

    private static PlayerGameModeStatPerGateway MakeStat(GameMode gameMode, Race? race)
    {
        var id = new BattleTagIdCombined(
            new List<PlayerId> { PlayerId.Create(BattleTag) },
            Gateway, gameMode, Season, race);
        return PlayerGameModeStatPerGateway.Create(id);
    }

    private static ProgressionMilestone MakeMilestone(GameMode gameMode, Race? race, int wins)
    {
        var m = ProgressionMilestone.Create(
            new List<PlayerId> { PlayerId.Create(BattleTag) },
            Gateway, gameMode, race);
        for (var i = 0; i < wins; i++)
        {
            m.RecordWin();
        }
        return m;
    }

    [Test]
    public async Task LoadPlayerStatsWithRanks_StampsMilestone_ReconstructingSeasonlessKey()
    {
        // 1v1 race-split stat (race in key) with a matching milestone doc.
        var soloStat = MakeStat(GameMode.GM_1v1, Race.HU);
        await _playerRepository.UpsertPlayerGameModeStatPerGateway(soloStat);
        await _milestoneRepository.UpsertMilestone(MakeMilestone(GameMode.GM_1v1, Race.HU, 53));

        // AT-mode stat (the stored gameMode is the _AT variant) with a matching milestone doc.
        var atStat = MakeStat(GameMode.GM_2v2_AT, null);
        await _playerRepository.UpsertPlayerGameModeStatPerGateway(atStat);
        await _milestoneRepository.UpsertMilestone(MakeMilestone(GameMode.GM_2v2_AT, null, 12));

        // 1v1 stat (different race) with NO milestone doc.
        var noMilestoneStat = MakeStat(GameMode.GM_1v1, Race.NE);
        await _playerRepository.UpsertPlayerGameModeStatPerGateway(noMilestoneStat);

        var stats = await _handler.LoadPlayerStatsWithRanks(BattleTag, Gateway, Season);

        var solo = stats.Single(s => s.GameMode == GameMode.GM_1v1 && s.Race == Race.HU);
        Assert.IsNotNull(solo.Milestone);
        Assert.AreEqual(53, solo.Milestone.CurrentWins);
        Assert.Greater(solo.Milestone.NextTarget, 53);

        var at = stats.Single(s => s.GameMode == GameMode.GM_2v2_AT);
        Assert.IsNotNull(at.Milestone);
        Assert.AreEqual(12, at.Milestone.CurrentWins);

        var none = stats.Single(s => s.GameMode == GameMode.GM_1v1 && s.Race == Race.NE);
        Assert.IsNull(none.Milestone);
    }
}
