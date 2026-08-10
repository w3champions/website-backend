using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using MongoDB.Driver;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;

namespace WC3ChampionsStatisticService.UnitTests.Search;

// Shared builders for the search suite. These tests run without Mongo — see README.md in this folder
// for why, and for the seam that makes it possible.
public static class SearchTestFixtures
{
    // A PersonalSetting is the unit of global-search's directory: matching runs over its Id (the
    // battleTag) and the response carries its ProfilePicture.
    public static PersonalSetting Setting(
        string battleTag,
        AvatarCategory race = AvatarCategory.HU,
        long pictureId = 1)
    {
        return new PersonalSetting(battleTag)
        {
            ProfilePicture = new ProfilePicture { Race = race, PictureId = pictureId },
        };
    }

    public static List<PersonalSetting> Settings(params string[] battleTags)
    {
        var list = new List<PersonalSetting>();
        foreach (var tag in battleTags)
        {
            list.Add(Setting(tag));
        }
        return list;
    }

    // Builds a PlayerService whose directory is exactly `directory`, with no database behind it.
    //
    // The seam: PersonalSettingsProvider.GetPersonalSettingsAsync delegates to
    // ICachedDataProvider.GetCachedOrRequestAsync(fetchFromMongo, key). Mocking the cache to return a
    // value WITHOUT invoking the callback means the Mongo fetch is never reached, so the MongoClient
    // handed to the provider is inert. MongoClient's constructor does not open a connection.
    public static PlayerService PlayerServiceWith(
        List<PersonalSetting> directory,
        Mock<IPlayerRepository> playerRepository = null,
        Mock<IRankRepository> rankRepository = null)
    {
        var cache = new Mock<ICachedDataProvider<List<PersonalSetting>>>();
        cache
            .Setup(c => c.GetCachedOrRequestAsync(
                It.IsAny<Func<Task<List<PersonalSetting>>>>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan?>()))
            .ReturnsAsync(directory);

        var settingsProvider = new PersonalSettingsProvider(
            new MongoClient("mongodb://localhost:27017"),
            cache.Object);

        var repo = playerRepository ?? EmptyPlayerRepository();
        var ranks = rankRepository ?? EmptyRankRepository();

        return new PlayerService(repo.Object, null, settingsProvider, ranks.Object);
    }

    // Default: nobody is ranked, so a context search puts every hit in the unranked block.
    public static Mock<IRankRepository> EmptyRankRepository()
    {
        return RankRepositoryWith();
    }

    // A rank repository whose ladder is exactly `ranks`, honouring the season/gateway/gameMode filter
    // the way the real methods do. Both are stubbed from the same ladder so a test cannot have the
    // search core and the enrichment call disagree about who is ranked — the real implementations
    // return the same row set, and a fixture that let them drift would hide that.
    public static Mock<IRankRepository> RankRepositoryWith(params Rank[] ranks)
    {
        var repo = new Mock<IRankRepository>();

        List<Rank> OnLadder(List<string> tags, int season, GateWay gateWay, GameMode gameMode) =>
            ranks.Where(r =>
                r.MemberIds.Any(tags.Contains)
                && r.Season == season
                && r.Gateway == gateWay
                && r.GameMode == gameMode).ToList();

        repo
            .Setup(r => r.LoadRanksForPlayers(
                It.IsAny<List<string>>(),
                It.IsAny<int>(),
                It.IsAny<GateWay>(),
                It.IsAny<GameMode>()))
            .ReturnsAsync((List<string> tags, int season, GateWay gateWay, GameMode gameMode) =>
                OnLadder(tags, season, gateWay, gameMode));

        repo
            .Setup(r => r.LoadLadderStandings(
                It.IsAny<List<string>>(),
                It.IsAny<int>(),
                It.IsAny<GateWay>(),
                It.IsAny<GameMode>()))
            .ReturnsAsync((List<string> tags, int season, GateWay gateWay, GameMode gameMode) =>
                OnLadder(tags, season, gateWay, gameMode)
                    .Select(r => new PlayerLadderStanding
                    {
                        MemberIds = r.MemberIds,
                        RankingPoints = r.RankingPoints,
                    })
                    .ToList());

        return repo;
    }

    // Default: no PlayerOverallStats for anyone, so Seasons stays empty.
    public static Mock<IPlayerRepository> EmptyPlayerRepository()
    {
        var repo = new Mock<IPlayerRepository>();
        repo
            .Setup(r => r.GetPlayerBattleTagsAsync(It.IsAny<ICollection<string>>()))
            .ReturnsAsync(new Dictionary<string, PlayerOverallStats>());
        return repo;
    }

    public static Mock<IPlayerRepository> PlayerRepositoryWithSeasons(
        Dictionary<string, PlayerOverallStats> statsByBattleTag)
    {
        var repo = new Mock<IPlayerRepository>();
        repo
            .Setup(r => r.GetPlayerBattleTagsAsync(It.IsAny<ICollection<string>>()))
            .ReturnsAsync(statsByBattleTag);
        return repo;
    }

    public static PlayerOverallStats StatsWithSeasons(string battleTag, params int[] seasonIds)
    {
        var stats = PlayerOverallStats.Create(battleTag);
        foreach (var id in seasonIds)
        {
            stats.ParticipatedInSeasons.Add(new Season(id));
        }
        return stats;
    }

    // An AT team rank of any size — members beyond the second exist only in MemberIds.
    public static Rank TeamRankFor(
        string[] members,
        int league = 1,
        int rankNumber = 5,
        double rankingPoints = 500,
        GateWay gateWay = GateWay.Europe,
        GameMode gameMode = GameMode.GM_2v2_AT,
        int season = 13)
    {
        return new Rank([.. members], league, rankNumber, rankingPoints, null, gateWay, gameMode, season)
        {
            Players =
            [
                PlayerOverview.Create(
                    members.Select(PlayerId.Create).ToList(),
                    gateWay,
                    gameMode,
                    season,
                    null),
            ],
        };
    }

    public static Rank RankFor(
        string battleTag,
        int league = 1,
        int rankNumber = 5,
        double rankingPoints = 500,
        Race? race = null,
        GateWay gateWay = GateWay.Europe,
        GameMode gameMode = GameMode.GM_1v1,
        int season = 13)
    {
        return new Rank([battleTag], league, rankNumber, rankingPoints, race, gateWay, gameMode, season)
        {
            Players =
            [
                PlayerOverview.Create(
                    [PlayerId.Create(battleTag)],
                    gateWay,
                    gameMode,
                    season,
                    race),
            ],
        };
    }

    // Body for POST api/ladder/ranks-for-players with the suite's defaults.
    public static RanksForPlayersRequest Request(
        List<string> battleTags,
        int season = 13,
        GateWay gateWay = GateWay.Europe,
        GameMode gameMode = GameMode.GM_1v1)
    {
        return new RanksForPlayersRequest
        {
            BattleTags = battleTags,
            Season = season,
            GateWay = gateWay,
            GameMode = gameMode,
        };
    }

    // The two controllers under test, with every dependency the search actions never reach nulled.
    public static PlayersController PlayersControllerWith(IPlayerRepository playerRepo = null, PlayerService playerService = null)
    {
        return new PlayersController(
            playerRepo ?? EmptyPlayerRepository().Object,
            null, // GameModeStatQueryHandler
            null, // IPersonalSettingsRepository
            null, // IClanRepository
            null, // PlayerAkaProvider
            playerService,
            null, // IBattleTagResolver
            null); // ChatDetailsQueryHandler
    }

    public static LadderController LadderControllerWith(IRankRepository rankRepo, IPlayerRepository playerRepo)
    {
        return new LadderController(rankRepo, playerRepo, null, null, null);
    }
}
