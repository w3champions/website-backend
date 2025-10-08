using W3ChampionsStatisticService.Common.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Matches;
using W3C.Contracts.GameObjects;
using W3ChampionsStatisticService.Ladder;
using Moq;
using W3ChampionsStatisticService.Services;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Heroes;

namespace WC3ChampionsStatisticService.Tests.Matchups;

[TestFixture]
public class MatchupRepoTests : IntegrationTestBase
{
    private MatchRepository matchRepository;
    private Mock<TracingService> tracingService;
    [SetUp]
    public async Task SetupSut()
    {
        tracingService = TestDtoHelper.CreateMockedTracingService();
        matchRepository = new MatchRepository(MongoClient, new OngoingMatchesCache(MongoClient, tracingService.Object));
        await matchRepository.EnsureIndices();
    }


    [Test]
    public async Task Load_WithMinMaxMmrFilter_ReturnsCorrectMatches()
    {
        var matchLowMmr = TestDtoHelper.CreateFakeEvent();
        matchLowMmr.match.players[0].mmr = new Mmr { rating = 900, rd = 30, vol = 0.06, rating_lower_bound = 820 };
        matchLowMmr.match.players[1].mmr = new Mmr { rating = 950, rd = 30, vol = 0.06, rating_lower_bound = 820 };
        var matchMidMmr = TestDtoHelper.CreateFakeEvent();
        matchMidMmr.match.players[0].mmr = new Mmr { rating = 1500, rd = 30, vol = 0.06, rating_lower_bound = 820 };
        matchMidMmr.match.players[1].mmr = new Mmr { rating = 1550, rd = 30, vol = 0.06, rating_lower_bound = 820 };
        var matchHighMmr = TestDtoHelper.CreateFakeEvent();
        matchHighMmr.match.players[0].mmr = new Mmr { rating = 2500, rd = 30, vol = 0.06, rating_lower_bound = 820 };
        matchHighMmr.match.players[1].mmr = new Mmr { rating = 2550, rd = 30, vol = 0.06, rating_lower_bound = 820 };

        var matchupLow = Matchup.Create(matchLowMmr);
        var matchupMid = Matchup.Create(matchMidMmr);
        var matchupHigh = Matchup.Create(matchHighMmr);

        // Set CurrentMmr for all players in each matchup
        for (int i = 0; i < matchupLow.Teams.Count; i++)
            for (int j = 0; j < matchupLow.Teams[i].Players.Count; j++)
                matchupLow.Teams[i].Players[j].CurrentMmr = (int)matchLowMmr.match.players[j].mmr.rating;
        for (int i = 0; i < matchupMid.Teams.Count; i++)
            for (int j = 0; j < matchupMid.Teams[i].Players.Count; j++)
                matchupMid.Teams[i].Players[j].CurrentMmr = (int)matchMidMmr.match.players[j].mmr.rating;
        for (int i = 0; i < matchupHigh.Teams.Count; i++)
            for (int j = 0; j < matchupHigh.Teams[i].Players.Count; j++)
                matchupHigh.Teams[i].Players[j].CurrentMmr = (int)matchHighMmr.match.players[j].mmr.rating;

        await matchRepository.Insert(matchupLow);
        await matchRepository.Insert(matchupMid);
        await matchRepository.Insert(matchupHigh);

        // Only mid MMR match should be returned
        var matches = await matchRepository.Load(matchLowMmr.match.season, matchLowMmr.match.gameMode, minMmr: 1200, maxMmr: 2000);
        Assert.AreEqual(1, matches.Count);
        Assert.IsTrue(matches.All(m => m.Teams.SelectMany(t => t.Players).Any(p => p.CurrentMmr >= 1200 && p.CurrentMmr <= 2000)));

        // Only high MMR match should be returned
        var matchesHigh = await matchRepository.Load(matchLowMmr.match.season, matchLowMmr.match.gameMode, minMmr: 2000, maxMmr: MmrConstants.MaxMmr);
        Assert.AreEqual(1, matchesHigh.Count);
        Assert.IsTrue(matchesHigh.All(m => m.Teams.SelectMany(t => t.Players).Any(p => p.CurrentMmr >= 2000 && p.CurrentMmr <= MmrConstants.MaxMmr)));

        // All matches should be returned for wide range
        var matchesAll = await matchRepository.Load(matchLowMmr.match.season, matchLowMmr.match.gameMode, minMmr: 0, maxMmr: MmrConstants.MaxMmr);
        Assert.AreEqual(3, matchesAll.Count);
    }

    [Test]
    public async Task Count_WithMinMaxMmrFilter_ReturnsCorrectCount()
    {
        var matchLowMmr = TestDtoHelper.CreateFakeEvent();
        matchLowMmr.match.players[0].mmr = new Mmr { rating = 900, rd = 30, vol = 0.06, rating_lower_bound = 820 };
        matchLowMmr.match.players[1].mmr = new Mmr { rating = 950, rd = 30, vol = 0.06, rating_lower_bound = 820 };
        var matchMidMmr = TestDtoHelper.CreateFakeEvent();
        matchMidMmr.match.players[0].mmr = new Mmr { rating = 1500, rd = 30, vol = 0.06, rating_lower_bound = 820 };
        matchMidMmr.match.players[1].mmr = new Mmr { rating = 1550, rd = 30, vol = 0.06, rating_lower_bound = 820 };
        var matchHighMmr = TestDtoHelper.CreateFakeEvent();
        matchHighMmr.match.players[0].mmr = new Mmr { rating = 2500, rd = 30, vol = 0.06, rating_lower_bound = 820 };
        matchHighMmr.match.players[1].mmr = new Mmr { rating = 2550, rd = 30, vol = 0.06, rating_lower_bound = 820 };

        var matchupLow = Matchup.Create(matchLowMmr);
        var matchupMid = Matchup.Create(matchMidMmr);
        var matchupHigh = Matchup.Create(matchHighMmr);

        // Set CurrentMmr for all players in each matchup
        for (int i = 0; i < matchupLow.Teams.Count; i++)
            for (int j = 0; j < matchupLow.Teams[i].Players.Count; j++)
                matchupLow.Teams[i].Players[j].CurrentMmr = (int)matchLowMmr.match.players[j].mmr.rating;
        for (int i = 0; i < matchupMid.Teams.Count; i++)
            for (int j = 0; j < matchupMid.Teams[i].Players.Count; j++)
                matchupMid.Teams[i].Players[j].CurrentMmr = (int)matchMidMmr.match.players[j].mmr.rating;
        for (int i = 0; i < matchupHigh.Teams.Count; i++)
            for (int j = 0; j < matchupHigh.Teams[i].Players.Count; j++)
                matchupHigh.Teams[i].Players[j].CurrentMmr = (int)matchHighMmr.match.players[j].mmr.rating;

        await matchRepository.Insert(matchupLow);
        await matchRepository.Insert(matchupMid);
        await matchRepository.Insert(matchupHigh);

        var countMid = await matchRepository.Count(matchLowMmr.match.season, matchLowMmr.match.gameMode, minMmr: 1200, maxMmr: 2000);
        Assert.AreEqual(1, countMid);

        var countHigh = await matchRepository.Count(matchLowMmr.match.season, matchLowMmr.match.gameMode, minMmr: 2000, maxMmr: MmrConstants.MaxMmr);
        Assert.AreEqual(1, countHigh);

        var countAll = await matchRepository.Count(matchLowMmr.match.season, matchLowMmr.match.gameMode, minMmr: 0, maxMmr: MmrConstants.MaxMmr);
        Assert.AreEqual(3, countAll);
    }

    [Test]
    public async Task LoadAndSave()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));
        var matches = await matchRepository.Load(matchFinishedEvent1.match.season, matchFinishedEvent1.match.gameMode);

        Assert.AreEqual(2, matches.Count);
    }

    [Test]
    public async Task LoadWithHeroFilter()
    {
        var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent.result.players.First().heroes = TestDtoHelper.CreateHeroList(
            new List<W3ChampionsStatisticService.Heroes.HeroType>
            {
                W3ChampionsStatisticService.Heroes.HeroType.Archmage,
                W3ChampionsStatisticService.Heroes.HeroType.BansheeRanger,
            }
        );
        matchFinishedEvent.result.players.Last().heroes = TestDtoHelper.CreateHeroList(
            new List<W3ChampionsStatisticService.Heroes.HeroType>
            {
                W3ChampionsStatisticService.Heroes.HeroType.Blademaster,
                W3ChampionsStatisticService.Heroes.HeroType.Farseer,
            }
        );

        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent2.result.players.First().heroes = TestDtoHelper.CreateHeroList(
            new List<W3ChampionsStatisticService.Heroes.HeroType>
            {
                W3ChampionsStatisticService.Heroes.HeroType.PriestessOfTheMoon,
                W3ChampionsStatisticService.Heroes.HeroType.BansheeRanger,
            }
        );
        matchFinishedEvent2.result.players.Last().heroes = TestDtoHelper.CreateHeroList(
            new List<W3ChampionsStatisticService.Heroes.HeroType>
            {
                W3ChampionsStatisticService.Heroes.HeroType.Blademaster,
                W3ChampionsStatisticService.Heroes.HeroType.Farseer,
                W3ChampionsStatisticService.Heroes.HeroType.DeathKnight,
            }
        );

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));
        var matches = await matchRepository.Load(
            matchFinishedEvent.match.season,
            matchFinishedEvent.match.gameMode,
            hero: W3ChampionsStatisticService.Heroes.HeroType.Archmage
        );

        Assert.AreEqual(1, matches.Count);
        Assert.AreEqual(matchFinishedEvent.Id, matches.First().Id);

        var firstPlayerHeroes = matches.First().Teams.First().Players.First().Heroes;
        var expectedHeroes = matchFinishedEvent.result.players.First().heroes.Select(h => new W3ChampionsStatisticService.Heroes.Hero(h)).ToList();
        Assert.AreEqual(expectedHeroes.Count, firstPlayerHeroes.Count);
        Assert.AreEqual(expectedHeroes.First().Id, firstPlayerHeroes.First().Id);
        Assert.AreEqual(expectedHeroes.Last().Id, firstPlayerHeroes.Last().Id);

        var secondPlayerHeroes = matches.First().Teams.Last().Players.First().Heroes;
        var secondExpectedHeroes = matchFinishedEvent.result.players.Last().heroes.Select(h => new W3ChampionsStatisticService.Heroes.Hero(h)).ToList();
        Assert.AreEqual(secondExpectedHeroes.Count, secondPlayerHeroes.Count);
        Assert.AreEqual(secondExpectedHeroes.First().Id, secondPlayerHeroes.First().Id);
        Assert.AreEqual(secondExpectedHeroes.Last().Id, secondPlayerHeroes.Last().Id);
    }

    [Test]
    public async Task LoadWithHeroFilterNoResults()
    {
        var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent.result.players.First().heroes = TestDtoHelper.CreateHeroList(
            new List<W3ChampionsStatisticService.Heroes.HeroType>
            {
                W3ChampionsStatisticService.Heroes.HeroType.Archmage,
                W3ChampionsStatisticService.Heroes.HeroType.BansheeRanger,
            }
        );
        matchFinishedEvent.result.players.Last().heroes = TestDtoHelper.CreateHeroList(
            new List<W3ChampionsStatisticService.Heroes.HeroType>
            {
                W3ChampionsStatisticService.Heroes.HeroType.Blademaster,
                W3ChampionsStatisticService.Heroes.HeroType.Farseer,
            }
        );

        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent2.result.players.First().heroes = TestDtoHelper.CreateHeroList(
            new List<W3ChampionsStatisticService.Heroes.HeroType>
            {
                W3ChampionsStatisticService.Heroes.HeroType.PriestessOfTheMoon,
                W3ChampionsStatisticService.Heroes.HeroType.BansheeRanger,
            }
        );
        matchFinishedEvent2.result.players.Last().heroes = TestDtoHelper.CreateHeroList(
            new List<W3ChampionsStatisticService.Heroes.HeroType>
            {
                W3ChampionsStatisticService.Heroes.HeroType.Blademaster,
                W3ChampionsStatisticService.Heroes.HeroType.Farseer,
                W3ChampionsStatisticService.Heroes.HeroType.DeathKnight,
            }
        );

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));
        var matches = await matchRepository.Load(
            matchFinishedEvent.match.season,
            matchFinishedEvent.match.gameMode,
            hero: W3ChampionsStatisticService.Heroes.HeroType.KeeperOfTheGrove
        );

        Assert.AreEqual(0, matches.Count);
    }

    [Test]
    public async Task LoadAndSearch()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent1.match.players[1].battleTag = "KOMISCHER#123";
        matchFinishedEvent1.match.players[1].won = true;
        matchFinishedEvent1.match.players[0].won = false;
        matchFinishedEvent1.match.gateway = GateWay.America;
        matchFinishedEvent1.match.season = 1;
        matchFinishedEvent2.match.gateway = GateWay.America;
        matchFinishedEvent2.match.season = 1;

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));

        var matches = await matchRepository.LoadFor("KOMISCHER#123");

        Assert.AreEqual(1, matches.Count);
        Assert.AreEqual("KOMISCHER#123", matches[0].Teams[0].Players[0].BattleTag);
    }

    [Test]
    public async Task LoadAndSearch_InvalidString()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent1.match.season = 1;
        matchFinishedEvent1.match.players[1].battleTag = "peter#123";

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));

        var matches = await matchRepository.LoadFor("asd#123");

        Assert.AreEqual(0, matches.Count);
    }

    [Test]
    public async Task Upsert()
    {
        var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent));
        var matches = await matchRepository.Load(matchFinishedEvent.match.season, matchFinishedEvent.match.gameMode);

        Assert.AreEqual(1, matches.Count);
    }

    [Test]
    public async Task CountFor()
    {
        for (int i = 0; i < 100; i++)
        {
            Console.WriteLine($"https://www.test.w3champions.com:{i}/login");
        }

        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent3 = TestDtoHelper.CreateFakeEvent();

        matchFinishedEvent1.match.season = 1;
        matchFinishedEvent1.match.players[0].battleTag = "peter#123";
        matchFinishedEvent1.match.players[1].battleTag = "wolf#456";

        matchFinishedEvent2.match.season = 1;
        matchFinishedEvent2.match.players[0].battleTag = "wolf#456";
        matchFinishedEvent2.match.players[1].battleTag = "peter#123";

        matchFinishedEvent3.match.season = 1;
        matchFinishedEvent3.match.players[0].battleTag = "notFound";
        matchFinishedEvent3.match.players[1].battleTag = "notFound2";

        var matchup = Matchup.Create(matchFinishedEvent1);
        await matchRepository.Insert(matchup);
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));
        var count = await matchRepository.CountFor(matchup.Teams[0].Players[0].BattleTag);

        Assert.AreEqual(2, count);
    }

    [Test]
    public async Task LoadFor_WithHeroFilter()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();

        // Target player participates in both matches
        matchFinishedEvent1.match.players[0].battleTag = "heroTester#111";
        matchFinishedEvent1.result.players[0].battleTag = "heroTester#111"; // ensure hero data maps
        matchFinishedEvent2.match.players[0].battleTag = "heroTester#111";
        matchFinishedEvent2.result.players[0].battleTag = "heroTester#111";
        // Ensure season matches repository default (1)
        matchFinishedEvent1.match.season = 1;
        matchFinishedEvent2.match.season = 1;

        // Assign heroes (first hero in list determines filter match)
        matchFinishedEvent1.result.players[0].heroes = TestDtoHelper.CreateHeroList(new List<HeroType>
        {
            HeroType.Archmage,
            HeroType.BansheeRanger
        });
        matchFinishedEvent2.result.players[0].heroes = TestDtoHelper.CreateHeroList(new List<HeroType>
        {
            HeroType.Blademaster,
            HeroType.Farseer
        });

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));

        var matchesArchmage = await matchRepository.LoadFor("heroTester#111", hero: HeroType.Archmage);
        Assert.AreEqual(1, matchesArchmage.Count, "Should return only matches where first hero matches filter");
        Assert.AreEqual(HeroType.Archmage, matchesArchmage[0].Teams.First().Players.First(p => p.BattleTag == "heroTester#111").Heroes.First().Id);

        var matchesBlade = await matchRepository.LoadFor("heroTester#111", hero: HeroType.Blademaster);
        Assert.AreEqual(1, matchesBlade.Count);
        Assert.AreEqual(HeroType.Blademaster, matchesBlade[0].Teams.First().Players.First(p => p.BattleTag == "heroTester#111").Heroes.First().Id);

        var matchesKeeper = await matchRepository.LoadFor("heroTester#111", hero: HeroType.KeeperOfTheGrove);
        Assert.AreEqual(0, matchesKeeper.Count, "No match should have Keeper as first hero for player");
    }

    [Test]
    public async Task CountFor_WithHeroFilter()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent3 = TestDtoHelper.CreateFakeEvent();

        matchFinishedEvent1.match.players[0].battleTag = "heroCounter#222";
        matchFinishedEvent1.result.players[0].battleTag = "heroCounter#222";
        matchFinishedEvent2.match.players[0].battleTag = "heroCounter#222";
        matchFinishedEvent2.result.players[0].battleTag = "heroCounter#222";
        matchFinishedEvent3.match.players[0].battleTag = "heroCounter#222";
        matchFinishedEvent3.result.players[0].battleTag = "heroCounter#222";
        matchFinishedEvent1.match.season = 1;
        matchFinishedEvent2.match.season = 1;
        matchFinishedEvent3.match.season = 1;

        matchFinishedEvent1.result.players[0].heroes = TestDtoHelper.CreateHeroList(new List<HeroType> { HeroType.Archmage, HeroType.BansheeRanger });
        matchFinishedEvent2.result.players[0].heroes = TestDtoHelper.CreateHeroList(new List<HeroType> { HeroType.Archmage, HeroType.Farseer });
        matchFinishedEvent3.result.players[0].heroes = TestDtoHelper.CreateHeroList(new List<HeroType> { HeroType.Blademaster, HeroType.Farseer });

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent3));

        var countArchmage = await matchRepository.CountFor("heroCounter#222", hero: HeroType.Archmage);
        Assert.AreEqual(2, countArchmage, "Two matches should have Archmage as first hero");

        var countBlade = await matchRepository.CountFor("heroCounter#222", hero: HeroType.Blademaster);
        Assert.AreEqual(1, countBlade);

        var countKeeper = await matchRepository.CountFor("heroCounter#222", hero: HeroType.KeeperOfTheGrove);
        Assert.AreEqual(0, countKeeper);

        // AllFilter should return all three
        var countAll = await matchRepository.CountFor("heroCounter#222", hero: HeroType.AllFilter);
        Assert.AreEqual(3, countAll);
    }

    [Test]
    public async Task SearchForPlayerAndOpponent()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();

        matchFinishedEvent1.match.players[0].battleTag = "peter#123";
        matchFinishedEvent1.match.players[1].battleTag = "wolf#456";
        matchFinishedEvent1.match.season = 1;
        matchFinishedEvent2.match.players[0].battleTag = "peter#123";
        matchFinishedEvent2.match.players[1].battleTag = "ANDERER#456";
        matchFinishedEvent2.match.season = 1;

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));
        var matches = await matchRepository.LoadFor("peter#123", "wolf#456");
        var count = await matchRepository.CountFor("peter#123", "wolf#456");

        Assert.AreEqual(1, count);
        Assert.AreEqual("peter#123", matches.Single().Teams.First().Players.Single().BattleTag);
        Assert.AreEqual("wolf#456", matches.Single().Teams.Last().Players.Single().BattleTag);
    }

    [Test]
    public async Task SearchForPlayerAndOpponent_FilterByGateway()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();

        matchFinishedEvent1.match.gateway = GateWay.America;
        matchFinishedEvent2.match.gateway = GateWay.Europe;

        matchFinishedEvent1.match.season = 1;
        matchFinishedEvent1.match.players[0].battleTag = "peter#123";
        matchFinishedEvent1.match.players[1].battleTag = "wolf#456";

        matchFinishedEvent2.match.season = 1;
        matchFinishedEvent2.match.players[0].battleTag = "peter#123";
        matchFinishedEvent2.match.players[1].battleTag = "ANDERER#456";

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));
        var matches = await matchRepository.LoadFor("peter#123", null, GateWay.America);
        var count = await matchRepository.CountFor("peter#123", null, GateWay.America);

        Assert.AreEqual(1, count);
        Assert.AreEqual("peter#123", matches.Single().Teams.First().Players.Single().BattleTag);
    }

    [Test]
    public async Task SearchForPlayerAndOppoRace()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent3 = TestDtoHelper.CreateFakeEvent();

        matchFinishedEvent1.match.players[0].race = Race.UD;
        matchFinishedEvent1.match.players[1].race = Race.HU;

        matchFinishedEvent2.match.players[0].race = Race.UD;
        matchFinishedEvent2.match.players[1].race = Race.NE;

        matchFinishedEvent3.match.players[0].race = Race.OC;
        matchFinishedEvent3.match.players[1].race = Race.HU;

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));
        var matches = await matchRepository.LoadFor("peter#123", null, GateWay.Europe, GameMode.GM_1v1, Race.UD, Race.HU, 100, 0, 0);
        var count = await matchRepository.CountFor("peter#123", null, GateWay.Europe, GameMode.GM_1v1, Race.UD, Race.HU, 0);

        Assert.AreEqual(1, count);
        Assert.AreEqual(Matchup.Create(matchFinishedEvent1).ToString(), matches.Single().ToString());
    }

    [TestCase(1, "ANDERER#456")]
    [TestCase(0, "wolf#456")]
    public async Task SearchForPlayerAndOpponent_FilterBySeason(int season, string playerTwo)
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();

        matchFinishedEvent1.match.season = 0;
        matchFinishedEvent1.match.players[0].battleTag = "peter#123";
        matchFinishedEvent1.match.players[1].battleTag = "wolf#456";

        matchFinishedEvent2.match.season = 1;
        matchFinishedEvent2.match.players[0].battleTag = "peter#123";
        matchFinishedEvent2.match.players[1].battleTag = "ANDERER#456";

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));
        var matches = await matchRepository.LoadFor("peter#123", null, season: season);
        var count = await matchRepository.CountFor("peter#123", null, season: season);

        Assert.AreEqual(1, count);
        Assert.AreEqual("peter#123", matches.Single().Teams.First().Players.Single().BattleTag);
        Assert.AreEqual(playerTwo, matches.Single().Teams.Last().Players.Single().BattleTag);
    }

    [TestCase(1)]
    [TestCase(0)]
    public async Task SearchForPlayerAndOpponent_FilterBySeason_NoResults(int season)
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();

        matchFinishedEvent1.match.season = season ^ 1;
        matchFinishedEvent1.match.players[0].battleTag = "peter#123";
        matchFinishedEvent1.match.players[1].battleTag = "ANDERER#456";

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        var matches = await matchRepository.LoadFor("peter#123", null, season: season);
        var count = await matchRepository.CountFor("peter#123", null, season: season);

        Assert.AreEqual(0, count);
    }

    [Test]
    public async Task SearchForPlayerAndOpponent_2v2_SameTeam()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFake2v2AtEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();

        matchFinishedEvent1.match.season = 1;
        matchFinishedEvent1.match.players[0].battleTag = "peter#123";
        matchFinishedEvent1.match.players[1].battleTag = "wolf#456";
        matchFinishedEvent1.match.players[2].battleTag = "LostTeam1#456";
        matchFinishedEvent1.match.players[3].battleTag = "LostTeam2#456";

        matchFinishedEvent2.match.season = 1;
        matchFinishedEvent2.match.players[0].battleTag = "peter#123";
        matchFinishedEvent2.match.players[1].battleTag = "ANDERER#456";

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));
        var matches = await matchRepository.LoadFor("peter#123@10", "wolf#456");
        var count = await matchRepository.CountFor("peter#123@10", "wolf#456");

        Assert.AreEqual(0, count);
    }

    [Test]
    public async Task SearchForPlayerAndOpponent_2v2()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFake2v2AtEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent1.match.season = 1;
        matchFinishedEvent1.match.players[0].battleTag = "peter#123";
        matchFinishedEvent1.match.players[0].team = 0;
        matchFinishedEvent1.match.players[1].battleTag = "LostTeam1#456";
        matchFinishedEvent1.match.players[1].team = 0;

        matchFinishedEvent1.match.players[2].battleTag = "wolf#456";
        matchFinishedEvent1.match.players[2].team = 1;
        matchFinishedEvent1.match.players[3].battleTag = "LostTeam2#456";
        matchFinishedEvent1.match.players[3].team = 2;

        matchFinishedEvent2.match.season = 1;
        matchFinishedEvent2.match.players[0].battleTag = "peter#123";
        matchFinishedEvent2.match.players[1].battleTag = "ANDERER#456";

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));
        var matches = await matchRepository.LoadFor("peter#123", "wolf#456");
        var count = await matchRepository.CountFor("peter#123", "wolf#456");

        Assert.AreEqual(1, count);
        Assert.AreEqual("peter#123", matches.Single().Teams[0].Players[0].BattleTag);
        Assert.AreEqual("wolf#456", matches.Single().Teams[1].Players[0].BattleTag);
    }

    [Test]
    public async Task SearchForPlayerAndOpponent_2v2And1V1()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFake2v2AtEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();

        matchFinishedEvent1.match.season = 1;
        matchFinishedEvent1.match.players[0].battleTag = "peter#123";
        matchFinishedEvent1.match.players[0].team = 0;
        matchFinishedEvent1.match.players[1].battleTag = "LostTeam1#456";
        matchFinishedEvent1.match.players[1].team = 0;

        matchFinishedEvent1.match.players[2].battleTag = "wolf#456";
        matchFinishedEvent1.match.players[2].team = 1;
        matchFinishedEvent1.match.players[3].battleTag = "LostTeam2#456";
        matchFinishedEvent1.match.players[3].team = 1;

        matchFinishedEvent2.match.season = 1;
        matchFinishedEvent2.match.players[0].battleTag = "peter#123";
        matchFinishedEvent2.match.players[1].battleTag = "wolf#456";

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));
        var matches = await matchRepository.LoadFor("peter#123", "wolf#456");
        var count = await matchRepository.CountFor("peter#123", "wolf#456");

        Assert.AreEqual(2, count);
        Assert.AreEqual("peter#123", matches[0].Teams[0].Players[0].BattleTag);
        Assert.AreEqual("peter#123", matches[1].Teams[0].Players[0].BattleTag);
    }

    [Test]
    public async Task SearchForGameMode2v2_NotFound()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent1.match.gameMode = GameMode.GM_1v1;

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        var matches = await matchRepository.Load(0, GameMode.GM_2v2_AT);

        Assert.AreEqual(0, matches.Count);
    }

    [Test]
    public async Task SearchForGameMode2v2_Found()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent1.match.gameMode = GameMode.GM_2v2_AT;

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        var matches = await matchRepository.Load(0, GameMode.GM_2v2_AT);

        Assert.AreEqual(1, matches.Count);
    }

    [Test]
    public async Task SearchForGameMode2v2_LoadDefault()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent1.match.gameMode = GameMode.GM_2v2_AT;

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
        var matches = await matchRepository.Load(matchFinishedEvent1.match.season, matchFinishedEvent1.match.gameMode);

        Assert.AreEqual(1, matches.Count);
    }

    [Test]
    public async Task ReforgedIconGetsReplaced()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();

        matchFinishedEvent1.match.players[0].battleTag = "peter#123";
        matchFinishedEvent1.result.players[0].battleTag = "peter#123";
        matchFinishedEvent1.result.players[0].heroes =
        [
            new() { icon = "jainasea" }
        ];

        await InsertMatchEvent(matchFinishedEvent1);

        await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));

        var result = await matchRepository.LoadFinishedMatchDetails(matchFinishedEvent1.Id);

        Assert.AreEqual("archmage", result.PlayerScores[0].Heroes[0].Icon);
    }

    [Test]
    public async Task Cache_AllOngoingMatches()
    {


        var storedEvent = TestDtoHelper.CreateFakeStartedEvent();
        await matchRepository.InsertOnGoingMatch(OnGoingMatchup.Create(storedEvent));

        await Task.Delay(100);

        var result = await matchRepository.LoadOnGoingMatches(
            storedEvent.match.gameMode,
            storedEvent.match.gateway);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(storedEvent.match.id, result[0].MatchId);

        var notCachedEvent = TestDtoHelper.CreateFakeStartedEvent();
        await matchRepository.InsertOnGoingMatch(OnGoingMatchup.Create(notCachedEvent));

        await Task.Delay(100);

        var result2 = await matchRepository.LoadOnGoingMatches(
            notCachedEvent.match.gameMode,
            notCachedEvent.match.gateway);

        Assert.AreEqual(2, result2.Count);
        Assert.AreEqual(storedEvent.match.id, result[0].MatchId);
    }

    [Test]
    public async Task Cache_ByPlayerId()
    {
        var storedEvent = TestDtoHelper.CreateFakeStartedEvent();
        await matchRepository.InsertOnGoingMatch(OnGoingMatchup.Create(storedEvent));

        await Task.Delay(100);

        var result = await matchRepository.TryLoadOnGoingMatchForPlayer(storedEvent.match.players[0].battleTag);

        Assert.IsNotNull(result);
        Assert.AreEqual(storedEvent.match.id, result.MatchId);

        var notCachedEvent = TestDtoHelper.CreateFakeStartedEvent();
        await matchRepository.InsertOnGoingMatch(OnGoingMatchup.Create(notCachedEvent));

        await Task.Delay(100);

        var result2 = await matchRepository.TryLoadOnGoingMatchForPlayer(storedEvent.match.players[0].battleTag);

        Assert.IsNotNull(result2);
        Assert.AreEqual(storedEvent.match.id, result.MatchId);
        Assert.AreEqual(notCachedEvent.match.id, result2.MatchId);
    }

    [Test]
    public async Task Cache_ByPlayerId_OneTeamEmpty()
    {
        var storedEvent = TestDtoHelper.Create1v1StartedEvent();

        await matchRepository.InsertOnGoingMatch(OnGoingMatchup.Create(storedEvent));

        await Task.Delay(100);

        var result = await matchRepository.TryLoadOnGoingMatchForPlayer(storedEvent.match.players[0].battleTag);

        Assert.IsNotNull(result);
        Assert.AreEqual(storedEvent.match.id, result.MatchId);

        var notCachedEvent = TestDtoHelper.Create1v1StartedEvent();
        await matchRepository.InsertOnGoingMatch(OnGoingMatchup.Create(notCachedEvent));

        await Task.Delay(100);

        var result2 = await matchRepository.TryLoadOnGoingMatchForPlayer(storedEvent.match.players[0].battleTag);

        Assert.IsNotNull(result2);
        Assert.AreEqual(storedEvent.match.id, result.MatchId);
        Assert.AreEqual(notCachedEvent.match.id, result2.MatchId);
    }

    [Test]
    public async Task LoadLastSeason()
    {
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        await rankRepository.UpsertSeason(new Season(1));
        await rankRepository.UpsertSeason(new Season(2));
        var result = await matchRepository.LoadLastSeason();
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Id);
    }
}
