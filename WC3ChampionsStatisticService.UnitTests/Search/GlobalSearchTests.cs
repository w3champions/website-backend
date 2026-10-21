using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles;
using static WC3ChampionsStatisticService.UnitTests.Search.SearchTestFixtures;

namespace WC3ChampionsStatisticService.UnitTests.Search;

// GET api/players/global-search — the consolidated core search.
//
// Contract under test is consumed by the website picker, the website ladder search, and the
// W3Champions launcher (launcher-e). Anything asserted here is load-bearing for at least one of them.
[TestFixture]
public class GlobalSearchTests
{
    // --- Matching -----------------------------------------------------------------------------

    [Test]
    public async Task Matches_Substring_AnywhereInBattleTag()
    {
        var service = PlayerServiceWith(Settings("Afternoon#1", "Grubby#2", "Moonlight#3"));

        var result = await service.GlobalSearchForPlayer("oon");

        CollectionAssert.AreEquivalent(
            new[] { "Afternoon#1", "Moonlight#3" },
            result.Select(r => r.BattleTag).ToList());
    }

    [Test]
    public async Task Matches_IsCaseInsensitive()
    {
        var service = PlayerServiceWith(Settings("MoOn#1"));

        var result = await service.GlobalSearchForPlayer("moon");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("MoOn#1", result[0].BattleTag);
    }

    [Test]
    public async Task Matches_NothingWhenNoTagContainsTheTerm()
    {
        var service = PlayerServiceWith(Settings("Grubby#1", "Happy#2"));

        var result = await service.GlobalSearchForPlayer("moon");

        Assert.IsEmpty(result);
    }

    // --- Relevance ranking --------------------------------------------------------------------
    //
    // Relevance is encoded as a numeric prefix on RelevanceId: 1 = exact name, 2 = name starts with
    // the term, 9 = substring elsewhere. Results are ordered by that string, so the tier drives
    // ordering AND doubles as the pagination cursor.

    [Test]
    public async Task Relevance_ExactBeatsPrefix_BeatsSubstring()
    {
        var service = PlayerServiceWith(Settings("Redmoon#4", "Moonlight#2", "Moon#1"));

        var result = await service.GlobalSearchForPlayer("moon");

        Assert.AreEqual(
            new[] { "Moon#1", "Moonlight#2", "Redmoon#4" },
            result.Select(r => r.BattleTag).ToArray());
    }

    [Test]
    public async Task Relevance_TierIsEncodedInRelevanceId()
    {
        var service = PlayerServiceWith(Settings("Moon#1", "Moonlight#2", "Redmoon#4"));

        var result = await service.GlobalSearchForPlayer("moon");

        Assert.AreEqual("1_Moon#1", result[0].RelevanceId);
        Assert.AreEqual("2_Moonlight#2", result[1].RelevanceId);
        Assert.AreEqual("9_Redmoon#4", result[2].RelevanceId);
    }

    [Test]
    public async Task Relevance_MatchIsOnNameOnly_NotTheNumericTag()
    {
        // "1234" appears only after the '#', so it is a substring hit (9), never exact or prefix.
        var service = PlayerServiceWith(Settings("Grubby#1234"));

        var result = await service.GlobalSearchForPlayer("1234");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("9_Grubby#1234", result[0].RelevanceId);
    }

    [Test]
    public async Task Relevance_WithinATierOrderingIsByBattleTag()
    {
        var service = PlayerServiceWith(Settings("MoonC#3", "MoonA#1", "MoonB#2"));

        var result = await service.GlobalSearchForPlayer("moon");

        Assert.AreEqual(
            new[] { "MoonA#1", "MoonB#2", "MoonC#3" },
            result.Select(r => r.BattleTag).ToArray());
    }

    // --- Paging -------------------------------------------------------------------------------
    //
    // Cursor paging, not offset paging. The caller echoes back the last RelevanceId it saw and the
    // server returns strictly-greater ones. launcher-e depends on this directly
    // (src/models/player-search.ts:43).

    [Test]
    public async Task Paging_HonoursPageSizeExactly()
    {
        // launcher-e sends pageSize=10 and treats "got exactly 10" as "there is another page".
        // A backend that silently returned its own default would end that launcher's paging at page 1.
        var service = PlayerServiceWith(Settings(
            Enumerable.Range(1, 30).Select(i => $"Moon{i:00}#{i}").ToArray()));

        var result = await service.GlobalSearchForPlayer("moon", pageSize: 10);

        Assert.AreEqual(10, result.Count);
    }

    [Test]
    public async Task Paging_CursorReturnsTheNextPageWithoutOverlap()
    {
        var directory = Settings(
            Enumerable.Range(1, 30).Select(i => $"Moon{i:00}#{i}").ToArray());
        var service = PlayerServiceWith(directory);

        var page1 = await service.GlobalSearchForPlayer("moon", pageSize: 10);
        var page2 = await service.GlobalSearchForPlayer(
            "moon",
            lastRelevanceId: page1.Last().RelevanceId,
            pageSize: 10);

        Assert.AreEqual(10, page2.Count);
        CollectionAssert.IsEmpty(
            page1.Select(r => r.BattleTag).Intersect(page2.Select(r => r.BattleTag)),
            "a cursor page must not repeat entries from the previous page");
    }

    [Test]
    public async Task Paging_EmptyCursorStartsAtTheFirstPage()
    {
        var service = PlayerServiceWith(Settings("MoonA#1", "MoonB#2"));

        var withEmptyCursor = await service.GlobalSearchForPlayer("moon", lastRelevanceId: "");

        Assert.AreEqual(2, withEmptyCursor.Count);
        Assert.AreEqual("MoonA#1", withEmptyCursor[0].BattleTag);
    }

    [Test]
    public async Task Paging_ExhaustedCursorReturnsEmpty()
    {
        var service = PlayerServiceWith(Settings("MoonA#1", "MoonB#2"));

        var page1 = await service.GlobalSearchForPlayer("moon", pageSize: 10);
        var page2 = await service.GlobalSearchForPlayer(
            "moon",
            lastRelevanceId: page1.Last().RelevanceId,
            pageSize: 10);

        Assert.IsEmpty(page2);
    }

    // --- Response shape -----------------------------------------------------------------------
    //
    // launcher-e reads battleTag, relevanceId, profilePicture.{race,pictureId,isClassic} and
    // seasons[].id. It declares `name` but never reads it. The website picker reads battleTag.

    [Test]
    public async Task Shape_EveryItemCarriesTheFieldsConsumersRead()
    {
        var service = PlayerServiceWith(Settings("Moon#1", "Moonlight#2"));

        var result = await service.GlobalSearchForPlayer("moon");

        foreach (var item in result)
        {
            Assert.IsNotNull(item.BattleTag, "battleTag is the identity every consumer emits");
            Assert.IsNotEmpty(item.RelevanceId, "relevanceId is the pagination cursor");
            Assert.IsNotNull(item.ProfilePicture, "launcher-e dereferences profilePicture without a null guard");
            Assert.IsNotNull(item.Seasons, "launcher-e maps over seasons without a null guard");
        }
    }

    [Test]
    public async Task Shape_NameIsTheBattleTagWithoutTheNumericSuffix()
    {
        var service = PlayerServiceWith(Settings("Moon#1234"));

        var result = await service.GlobalSearchForPlayer("moon");

        Assert.AreEqual("Moon", result[0].Name);
        Assert.AreEqual("Moon#1234", result[0].BattleTag);
    }

    [Test]
    public async Task Shape_ProfilePictureIsCarriedThrough()
    {
        var service = PlayerServiceWith([Setting("Moon#1", AvatarCategory.NE, 7)]);

        var result = await service.GlobalSearchForPlayer("moon");

        Assert.AreEqual(AvatarCategory.NE, result[0].ProfilePicture.Race);
        Assert.AreEqual(7, result[0].ProfilePicture.PictureId);
    }

    [Test]
    public async Task Shape_SeasonsAreEmptyWhenThePlayerHasNoOverallStats()
    {
        var service = PlayerServiceWith(Settings("Moon#1"));

        var result = await service.GlobalSearchForPlayer("moon");

        Assert.IsNotNull(result[0].Seasons);
        Assert.IsEmpty(result[0].Seasons);
    }

    // --- Seasons decoration -------------------------------------------------------------------

    [Test]
    public async Task Seasons_AreTheThreeMostRecent_Descending()
    {
        var stats = new Dictionary<string, PlayerOverallStats>
        {
            ["Moon#1"] = StatsWithSeasons("Moon#1", 1, 5, 3, 13, 9),
        };
        var service = PlayerServiceWith(Settings("Moon#1"), PlayerRepositoryWithSeasons(stats));

        var result = await service.GlobalSearchForPlayer("moon");

        Assert.AreEqual(
            new[] { 13, 9, 5 },
            result[0].Seasons.Select(s => s.Id).ToArray());
    }

    [Test]
    public async Task Seasons_AreLookedUpOnlyForThePageReturned()
    {
        // The seasons lookup happens after paging, so a 3000-row match still costs one bounded
        // lookup of pageSize keys rather than 3000.
        var directory = Settings(
            Enumerable.Range(1, 100).Select(i => $"Moon{i:000}#{i}").ToArray());
        var repo = EmptyPlayerRepository();
        var service = PlayerServiceWith(directory, repo);

        await service.GlobalSearchForPlayer("moon", pageSize: 10);

        repo.Verify(
            r => r.GetPlayerBattleTagsAsync(It.Is<ICollection<string>>(tags => tags.Count == 10)),
            Times.Once);
    }

    // --- Ladder context -----------------------------------------------------------------------
    //
    // Passing season + gateWay + gameMode searches in a ladder context, where standing on that ladder
    // is part of relevance. This exists because ladder standing is orthogonal to name relevance: a
    // page cut on name alone samples the ranked players effectively at random, so the ladder search
    // missed most of the ranked players matching a term. Context makes the cut land on the ranked
    // block first and pagination drain it in ladder order.

    [Test]
    public async Task Context_RankedSortsAboveUnranked_EvenWithWorseNameRelevance()
    {
        // "Moon#1" is an exact name match and "Redmoon#2" a mere substring hit, but on this ladder
        // only Redmoon is ranked — and on a ladder, that is the more relevant hit.
        var service = PlayerServiceWith(
            Settings("Moon#1", "Redmoon#2"),
            rankRepository: RankRepositoryWith(RankFor("Redmoon#2")));

        var result = await service.GlobalSearchForPlayer(
            "moon", season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        Assert.AreEqual(
            new[] { "Redmoon#2", "Moon#1" },
            result.Select(r => r.BattleTag).ToArray());
    }

    [Test]
    public async Task Context_RankedBlockIsOrderedByRankingPoints()
    {
        var service = PlayerServiceWith(
            Settings("MoonA#1", "MoonB#2", "MoonC#3"),
            rankRepository: RankRepositoryWith(
                RankFor("MoonA#1", rankingPoints: 12.6),
                RankFor("MoonB#2", rankingPoints: 47.8),
                RankFor("MoonC#3", rankingPoints: 30.1)));

        var result = await service.GlobalSearchForPlayer(
            "moon", season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        Assert.AreEqual(
            new[] { "MoonB#2", "MoonC#3", "MoonA#1" },
            result.Select(r => r.BattleTag).ToArray());
    }

    [Test]
    public async Task Context_AppendedDivisionOrdersByStanding_NotLeagueId()
    {
        // Divisions created mid-season take the next free league id: prod s13/EU/1v1 files Diamond
        // division 8 as league 52, behind Grass's 49–51. Ranking points carry the real ordering,
        // so the Diamond player leads whatever the ids say.
        var service = PlayerServiceWith(
            Settings("MoonDia#1", "MoonGrass#2"),
            rankRepository: RankRepositoryWith(
                RankFor("MoonDia#1", league: 52, rankNumber: 32, rankingPoints: 30.1),
                RankFor("MoonGrass#2", league: 49, rankNumber: 1, rankingPoints: 5.3)));

        var result = await service.GlobalSearchForPlayer(
            "moon", season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        Assert.AreEqual(
            new[] { "MoonDia#1", "MoonGrass#2" },
            result.Select(r => r.BattleTag).ToArray());
    }

    [Test]
    public async Task Context_NumericPartsSortNumerically_NotLexically()
    {
        // The key is compared as a string because it doubles as the cursor, so the complement is
        // zero-padded to a fixed five digits. Near the ceiling the complement gets short (999.5
        // points → 49), and unpadded it would sort behind the longer complement of a lower score.
        var service = PlayerServiceWith(
            Settings("MoonA#1", "MoonB#2"),
            rankRepository: RankRepositoryWith(
                RankFor("MoonA#1", rankingPoints: 999.5),
                RankFor("MoonB#2", rankingPoints: 960)));

        var result = await service.GlobalSearchForPlayer(
            "moon", season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        Assert.AreEqual(
            new[] { "MoonA#1", "MoonB#2" },
            result.Select(r => r.BattleTag).ToArray());
    }

    [Test]
    public async Task Context_RankingPointsOutsideTheKeyRangeAreClamped()
    {
        // The complement has exactly five digits, so points past 999.99 or below zero would
        // otherwise leave the key's namespace and break the string comparison the cursor relies on.
        // Clamped, the extremes still order correctly and still mint well-formed cursors.
        var service = PlayerServiceWith(
            Settings("MoonHigh#1", "MoonMid#2", "MoonNeg#3"),
            rankRepository: RankRepositoryWith(
                RankFor("MoonNeg#3", rankingPoints: -5),
                RankFor("MoonMid#2", rankingPoints: 500),
                RankFor("MoonHigh#1", rankingPoints: 1200)));

        var result = await service.GlobalSearchForPlayer(
            "moon", season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        Assert.AreEqual("0_00000_MoonHigh#1", result[0].RelevanceId, "past the ceiling clamps to the best key");
        Assert.AreEqual("0_49999_MoonMid#2", result[1].RelevanceId);
        Assert.AreEqual("0_99999_MoonNeg#3", result[2].RelevanceId, "below zero clamps to the worst key");
    }

    [Test]
    public async Task Context_EqualRankingPointsFallBackToBattleTagOrder()
    {
        // Equal points produce equal complements, so the battleTag suffix is what keeps the key —
        // and with it the cursor — deterministic between requests.
        var service = PlayerServiceWith(
            Settings("MoonA#1", "MoonB#2"),
            rankRepository: RankRepositoryWith(
                RankFor("MoonB#2", rankingPoints: 42.5),
                RankFor("MoonA#1", rankingPoints: 42.5)));

        var result = await service.GlobalSearchForPlayer(
            "moon", season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        Assert.AreEqual(
            new[] { "MoonA#1", "MoonB#2" },
            result.Select(r => r.BattleTag).ToArray());
    }

    [Test]
    public async Task Context_UnrankedTailKeepsNameRelevanceOrder()
    {
        var service = PlayerServiceWith(
            Settings("Redmoon#3", "Moon#1", "Moonlight#2"),
            rankRepository: EmptyRankRepository());

        var result = await service.GlobalSearchForPlayer(
            "moon", season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        Assert.AreEqual(
            new[] { "Moon#1", "Moonlight#2", "Redmoon#3" },
            result.Select(r => r.BattleTag).ToArray());
    }

    [Test]
    public async Task Context_RelevanceIdEncodesTheBlockItBelongsTo()
    {
        var service = PlayerServiceWith(
            Settings("Moon#1", "Moonlight#2"),
            rankRepository: RankRepositoryWith(RankFor("Moonlight#2", rankingPoints: 42.5)));

        var result = await service.GlobalSearchForPlayer(
            "moon", season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        // 95749 = 99999 - 42.5 * 100: the zero-padded complement, so higher points sort first.
        Assert.AreEqual("0_95749_Moonlight#2", result[0].RelevanceId, "ranked: block 0, then ladder standing");
        Assert.AreEqual("1_1_Moon#1", result[1].RelevanceId, "unranked: block 1, then name relevance");
    }

    [Test]
    public async Task Context_OnlyCountsRanksOnTheLadderAsked_For()
    {
        // Ranked in 2v2 does not make you ranked on the 1v1 ladder being searched.
        var service = PlayerServiceWith(
            Settings("Moonlight#2"),
            rankRepository: RankRepositoryWith(
                RankFor("Moonlight#2", gameMode: GameMode.GM_2v2_AT)));

        var result = await service.GlobalSearchForPlayer(
            "moon", season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        Assert.AreEqual("1_2_Moonlight#2", result[0].RelevanceId, "wrong-ladder rank leaves them unranked here");
    }

    [Test]
    public async Task Context_ATTeamRankStandsForBothMembers()
    {
        var service = PlayerServiceWith(
            Settings("MoonMate#1", "MoonBoss#2", "MoonSolo#3"),
            rankRepository: RankRepositoryWith(
                TeamRankFor(["MoonBoss#2", "MoonMate#1"], league: 1, rankNumber: 3)));

        var result = await service.GlobalSearchForPlayer(
            "moon", season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_2v2_AT);

        // Both members are ranked at the team's position; only the unpartnered player falls to the tail.
        Assert.AreEqual("MoonSolo#3", result[2].BattleTag);
        CollectionAssert.AreEquivalent(
            new[] { "MoonBoss#2", "MoonMate#1" },
            result.Take(2).Select(r => r.BattleTag).ToArray());
    }

    [Test]
    public async Task Context_TeamRankStandsForMembersBeyondTheSecond()
    {
        // Rank keeps its first two members in dedicated fields; a third exists only in MemberIds.
        var service = PlayerServiceWith(
            Settings("MoonMate#1", "MoonBoss#2", "MoonThird#3", "MoonSolo#4"),
            rankRepository: RankRepositoryWith(
                TeamRankFor(["MoonBoss#2", "MoonMate#1", "MoonThird#3"], gameMode: GameMode.GM_4v4_AT)));

        var result = await service.GlobalSearchForPlayer(
            "moon", season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_4v4_AT);

        // All three members are ranked at the team's position; only the teamless player is unranked.
        Assert.AreEqual("MoonSolo#4", result[3].BattleTag);
        CollectionAssert.AreEquivalent(
            new[] { "MoonBoss#2", "MoonMate#1", "MoonThird#3" },
            result.Take(3).Select(r => r.BattleTag).ToArray());
    }

    [Test]
    public async Task Context_PlayerHoldingSeveralRanksIsPlacedByTheirBest()
    {
        // 1v1 is ranked per race, so one player legitimately holds several ranks in one context.
        var service = PlayerServiceWith(
            Settings("MoonA#1", "MoonB#2"),
            rankRepository: RankRepositoryWith(
                RankFor("MoonA#1", rankingPoints: 22.1, race: Race.HU),
                RankFor("MoonA#1", rankingPoints: 47.8, race: Race.UD),
                RankFor("MoonB#2", rankingPoints: 33.4)));

        var result = await service.GlobalSearchForPlayer(
            "moon", season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        Assert.AreEqual(
            new[] { "MoonA#1", "MoonB#2" },
            result.Select(r => r.BattleTag).ToArray(),
            "MoonA's 47.8-point rank should place them, not their 22.1-point one");
    }

    [Test]
    public async Task Context_PagingRunsThroughTheRankedBlockIntoTheTail()
    {
        var service = PlayerServiceWith(
            Settings("MoonA#1", "MoonB#2", "MoonC#3", "MoonD#4"),
            rankRepository: RankRepositoryWith(
                RankFor("MoonC#3", rankingPoints: 47.8),
                RankFor("MoonD#4", rankingPoints: 30.1)));

        var page1 = await service.GlobalSearchForPlayer(
            "moon", pageSize: 2, season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);
        var page2 = await service.GlobalSearchForPlayer(
            "moon", lastRelevanceId: page1.Last().RelevanceId, pageSize: 2,
            season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        Assert.AreEqual(new[] { "MoonC#3", "MoonD#4" }, page1.Select(r => r.BattleTag).ToArray());
        Assert.AreEqual(new[] { "MoonA#1", "MoonB#2" }, page2.Select(r => r.BattleTag).ToArray());
    }

    [Test]
    public async Task Context_RankedPlayersBeyondThePageSurfaceThatNameOrderWouldBury()
    {
        // The defect this feature fixes, in miniature: one ranked player behind a wall of
        // better-name-matching unranked ones. Without context they fall off the page entirely.
        var directory = Settings(
            Enumerable.Range(1, 30).Select(i => $"Moon{i:00}#{i}").Append("Redmoon#99").ToArray());
        var ranked = RankRepositoryWith(RankFor("Redmoon#99", league: 1, rankNumber: 1));

        var withoutContext = await PlayerServiceWith(directory, rankRepository: ranked)
            .GlobalSearchForPlayer("moon", pageSize: 20);
        var withContext = await PlayerServiceWith(directory, rankRepository: ranked)
            .GlobalSearchForPlayer(
                "moon", pageSize: 20, season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        CollectionAssert.DoesNotContain(withoutContext.Select(r => r.BattleTag).ToList(), "Redmoon#99");
        Assert.AreEqual("Redmoon#99", withContext[0].BattleTag);
    }

    // --- Context is opt-in --------------------------------------------------------------------

    [Test]
    public async Task Context_OmittedLeavesRelevanceIdsExactlyAsTheyWere()
    {
        // launcher-e holds relevanceId as an opaque cursor and sends no context. Its key shape must
        // not move.
        var service = PlayerServiceWith(
            Settings("Moon#1", "Moonlight#2", "Redmoon#3"),
            rankRepository: RankRepositoryWith(RankFor("Redmoon#3")));

        var result = await service.GlobalSearchForPlayer("moon");

        Assert.AreEqual(
            new[] { "1_Moon#1", "2_Moonlight#2", "9_Redmoon#3" },
            result.Select(r => r.RelevanceId).ToArray());
    }

    [Test]
    public async Task Context_IsIgnoredUnlessAllThreePartsAreGiven()
    {
        // The three travel together; a partial context is not a ladder.
        var service = PlayerServiceWith(
            Settings("Moon#1", "Redmoon#2"),
            rankRepository: RankRepositoryWith(RankFor("Redmoon#2")));

        var seasonOnly = await service.GlobalSearchForPlayer("moon", season: 13);
        var noGameMode = await service.GlobalSearchForPlayer("moon", season: 13, gateWay: GateWay.Europe);

        Assert.AreEqual("1_Moon#1", seasonOnly[0].RelevanceId);
        Assert.AreEqual("1_Moon#1", noGameMode[0].RelevanceId);
    }

    [Test]
    public async Task Context_DoesNotHitTheLadderWhenNothingMatched()
    {
        var ranks = RankRepositoryWith(RankFor("Grubby#1"));
        var service = PlayerServiceWith(Settings("Grubby#1"), rankRepository: ranks);

        await service.GlobalSearchForPlayer(
            "nobodyhasthisname", season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        ranks.Verify(
            r => r.LoadLadderStandings(
                It.IsAny<List<string>>(), It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()),
            Times.Never);
    }

    [Test]
    public async Task Context_AsksOnlyForLadderPosition_NotTheFullRankRows()
    {
        // Ordering reads RankingPoints and nothing else, so the core takes the projected
        // query. Falling back to LoadRanksForPlayers would hydrate a joined PlayerOverview for every
        // hit in the match set — payload nothing here opens.
        var ranks = RankRepositoryWith(RankFor("Moon#1"));
        var service = PlayerServiceWith(Settings("Moon#1"), rankRepository: ranks);

        await service.GlobalSearchForPlayer(
            "moon", season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        ranks.Verify(
            r => r.LoadRanksForPlayers(
                It.IsAny<List<string>>(), It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()),
            Times.Never);
    }

    [Test]
    public async Task Context_LooksUpTheLadderOnceForTheWholeMatchSet()
    {
        // Standing has to be known for every match before the page is cut — that is the whole point —
        // but it costs one lookup, not one per candidate.
        var directory = Settings(
            Enumerable.Range(1, 50).Select(i => $"Moon{i:00}#{i}").ToArray());
        var ranks = RankRepositoryWith();
        var service = PlayerServiceWith(directory, rankRepository: ranks);

        await service.GlobalSearchForPlayer(
            "moon", pageSize: 10, season: 13, gateWay: GateWay.Europe, gameMode: GameMode.GM_1v1);

        ranks.Verify(
            r => r.LoadLadderStandings(
                It.Is<List<string>>(tags => tags.Count == 50),
                13, GateWay.Europe, GameMode.GM_1v1),
            Times.Once);
    }
}
