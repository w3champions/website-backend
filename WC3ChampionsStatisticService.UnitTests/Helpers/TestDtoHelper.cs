using System;
using System.Collections.Generic;
using System.Linq;
using AutoFixture;
using MongoDB.Bson;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;
using W3C.Contracts.Matchmaking;
using W3C.Contracts.GameObjects;
using Moq;
using W3ChampionsStatisticService.Services;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PersonalSettings;
using MongoDB.Driver;

namespace WC3ChampionsStatisticService.Tests;

public static class TestDtoHelper
{
    public static MatchFinishedEvent CreateFakeEvent()
    {
        var fixture = new Fixture { RepeatCount = 2 };
        fixture.Customize<PlayerMMrChange>(c => c.Without(p => p.matchRanking));
        var fakeEvent = fixture.Build<MatchFinishedEvent>().With(e => e.Id, ObjectId.GenerateNewId()).Create();

        var name1 = "peter#123";
        var name2 = "wolf#456";

        fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";

        fakeEvent.WasFakeEvent = false;
        fakeEvent.WasFromSync = false;

        fakeEvent.match.gateway = GateWay.Europe;
        fakeEvent.match.gameMode = GameMode.GM_1v1;
        fakeEvent.match.season = 0;

        fakeEvent.match.players.First().battleTag = name1;
        fakeEvent.match.players.First().won = true;
        fakeEvent.match.players.Last().battleTag = name2;
        fakeEvent.match.players.Last().won = false;

        fakeEvent.result.players.First().battleTag = name1;
        fakeEvent.result.players.Last().battleTag = name2;

        return fakeEvent;
    }

    public static MatchFinishedEvent CreateFake2v2AtEvent()
    {
        var fixture = new Fixture { RepeatCount = 4 };
        fixture.Customize<PlayerMMrChange>(c => c.Without(p => p.matchRanking));
        var fakeEvent = fixture.Build<MatchFinishedEvent>().With(e => e.Id, ObjectId.GenerateNewId()).Create();

        fakeEvent.WasFakeEvent = false;
        fakeEvent.WasFromSync = false;

        var name1 = "peter#123";
        var name2 = "wolf#456";
        var name3 = "TEAM2#123";
        var name4 = "TEAM2#456";

        fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";

        fakeEvent.match.gateway = GateWay.America;
        fakeEvent.match.gameMode = GameMode.GM_2v2;
        fakeEvent.match.season = 0;

        fakeEvent.match.players[0].battleTag = name1;
        fakeEvent.match.players[0].won = true;
        fakeEvent.match.players[0].team = 0;
        fakeEvent.match.players[0].atTeamId = "t1";

        fakeEvent.match.players[1].battleTag = name2;
        fakeEvent.match.players[1].won = true;
        fakeEvent.match.players[1].team = 0;
        fakeEvent.match.players[1].atTeamId = "t1";

        fakeEvent.match.players[2].battleTag = name3;
        fakeEvent.match.players[2].won = false;
        fakeEvent.match.players[2].team = 1;
        fakeEvent.match.players[2].atTeamId = "t2";

        fakeEvent.match.players[3].battleTag = name4;
        fakeEvent.match.players[3].won = false;
        fakeEvent.match.players[3].team = 1;
        fakeEvent.match.players[3].atTeamId = "t2";

        return fakeEvent;
    }


    public static MatchFinishedEvent CreateFakeFFAEvent()
    {
        var fixture = new Fixture { RepeatCount = 4 };
        fixture.Customize<PlayerMMrChange>(c => c.Without(p => p.matchRanking));
        var fakeEvent = fixture.Build<MatchFinishedEvent>().With(e => e.Id, ObjectId.GenerateNewId()).Create();

        fakeEvent.WasFakeEvent = false;
        fakeEvent.WasFromSync = false;

        var name1 = "peter#123";
        var name2 = "wolf#456";
        var name3 = "TEAM2#123";
        var name4 = "TEAM2#456";

        fakeEvent.match.map = "Maps/frozenthrone/community/(4)losttemple.w3x";

        fakeEvent.match.gateway = GateWay.Europe;
        fakeEvent.match.gameMode = GameMode.FFA;
        fakeEvent.match.season = 0;

        fakeEvent.match.players[0].battleTag = name1;
        fakeEvent.match.players[0].team = 0;
        fakeEvent.match.players[0].won = true;

        fakeEvent.match.players[1].battleTag = name2;
        fakeEvent.match.players[1].won = false;
        fakeEvent.match.players[1].team = 1;

        fakeEvent.match.players[2].battleTag = name3;
        fakeEvent.match.players[2].won = false;
        fakeEvent.match.players[2].team = 2;

        fakeEvent.match.players[3].battleTag = name4;
        fakeEvent.match.players[3].won = false;
        fakeEvent.match.players[3].team = 3;

        return fakeEvent;
    }

    public static MatchFinishedEvent CreateFakeFootmenFrenzyEvent()
    {
        var fixture = new Fixture();
        fixture.Customize<PlayerMMrChange>(c => c.Without(p => p.matchRanking));
        var fakeEvent = fixture.Build<MatchFinishedEvent>().With(e => e.Id, ObjectId.GenerateNewId()).Create();

        fakeEvent.WasFakeEvent = false;
        fakeEvent.WasFromSync = false;

        fakeEvent.match.players = new List<PlayerMMrChange>();
        CreateMatchTeam(fakeEvent, won: false, team: 0, playersPerTeam: 3);
        CreateMatchTeam(fakeEvent, won: true, team: 1, playersPerTeam: 3);
        CreateMatchTeam(fakeEvent, won: false, team: 2, playersPerTeam: 3);
        CreateMatchTeam(fakeEvent, won: false, team: 3, playersPerTeam: 3);

        fakeEvent.match.season = 0;
        fakeEvent.match.gameMode = GameMode.GM_FOOTMEN_FRENZY;
        fakeEvent.match.gateway = GateWay.Europe;
        fakeEvent.match.map = "W3Champions/Custom/Footmen_Frenzy_v5.8.0_W3C.w3x";

        return fakeEvent;
    }

    private static void CreateMatchTeam(MatchFinishedEvent fakeEvent, bool won, int team, int playersPerTeam)
    {
        CreateMatchTeamWithRanking(fakeEvent, won, team, playersPerTeam);
    }

    private static void CreateMatchTeamWithRanking(MatchFinishedEvent fakeEvent, bool won, int team, int playersPerTeam, int? matchRanking = null, string teamName = null)
    {
        var finalTeamName = teamName ?? $"Team{team}";
        for (int i = 0; i < playersPerTeam; i++)
        {
            var player = new PlayerMMrChange()
            {
                battleTag = $"{finalTeamName}Player{i + 1}#{team}{i}",
                team = team,
                won = won,
                matchRanking = matchRanking,
                mmr = new Mmr { rating = 1500.0 },
                updatedMmr = new Mmr { rating = won ? 1520.0 : 1480.0 },
                race = Race.HU,
                rndRace = Race.RnD
            };

            fakeEvent.match.players.Add(player);
        }
    }

    public static MatchFinishedEvent CreateFake2v2RTEvent()
    {
        var fixture = new Fixture { RepeatCount = 4 };
        fixture.Customize<PlayerMMrChange>(c => c.Without(p => p.matchRanking));
        var fakeEvent = fixture.Build<MatchFinishedEvent>().With(e => e.Id, ObjectId.GenerateNewId()).Create();

        fakeEvent.WasFakeEvent = false;
        fakeEvent.WasFromSync = false;

        var name1 = "peter#123";
        var name2 = "wolf#456";
        var name3 = "TEAM2#123";
        var name4 = "TEAM2#456";

        fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";

        fakeEvent.match.gateway = GateWay.Europe;
        fakeEvent.match.gameMode = GameMode.GM_2v2;
        fakeEvent.match.season = 0;

        fakeEvent.match.players[0].battleTag = name1;
        fakeEvent.match.players[0].won = true;
        fakeEvent.match.players[0].team = 0;
        fakeEvent.match.players[0].atTeamId = null;

        fakeEvent.match.players[1].battleTag = name2;
        fakeEvent.match.players[1].won = true;
        fakeEvent.match.players[1].team = 0;
        fakeEvent.match.players[1].atTeamId = null;

        fakeEvent.match.players[2].battleTag = name3;
        fakeEvent.match.players[2].won = false;
        fakeEvent.match.players[2].team = 1;
        fakeEvent.match.players[2].atTeamId = null;

        fakeEvent.match.players[3].battleTag = name4;
        fakeEvent.match.players[3].won = false;
        fakeEvent.match.players[3].team = 1;
        fakeEvent.match.players[3].atTeamId = null;

        return fakeEvent;
    }

    public static MatchFinishedEvent CreateFake4v4Event()
    {
        var fixture = new Fixture { RepeatCount = 8 };
        fixture.Customize<PlayerMMrChange>(c => c.Without(p => p.matchRanking));
        var fakeEvent = fixture.Build<MatchFinishedEvent>().With(e => e.Id, ObjectId.GenerateNewId()).Create();

        fakeEvent.WasFakeEvent = false;
        fakeEvent.WasFromSync = false;

        fakeEvent.match.map = "Maps/frozenthrone/community/(8)avalanche.w3x";
        fakeEvent.match.gateway = GateWay.Europe;
        fakeEvent.match.gameMode = GameMode.GM_4v4;
        fakeEvent.match.season = 0;

        // Initialize 8 players for 4v4
        for (int i = 0; i < 8; i++)
        {
            fakeEvent.match.players[i].battleTag = $"player{i}#123";
            fakeEvent.match.players[i].team = i < 4 ? 0 : 1;
            fakeEvent.match.players[i].won = i < 4;
            fakeEvent.match.players[i].race = Race.HU;
            fakeEvent.match.players[i].atTeamId = null;
        }

        return fakeEvent;
    }

    public static RankingChangedEvent CreateRankChangedEvent(string battleTag = "peTer#123")
    {
        return new RankingChangedEvent
        {
            gameMode = GameMode.GM_1v1,
            gateway = GateWay.America,
            season = 0,
            league = 1,
            id = 10010,
            ranks = new[]
            {
                new RankRaw
                {
                    rp = 14,
                    battleTags = new List<string> { battleTag }
                }
            }
        };
    }

    public static MatchStartedEvent CreateFakeStartedEvent()
    {
        var fixture = new Fixture { RepeatCount = 4 };
        var fakeEvent = fixture.Build<MatchStartedEvent>().With(e => e.Id, ObjectId.GenerateNewId()).Create();

        var name1 = "peter#123";
        var name2 = "wolf#456";
        var name3 = "TEAM2#123";
        var name4 = "TEAM2#456";

        fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";

        fakeEvent.match.gateway = GateWay.America;
        fakeEvent.match.gameMode = GameMode.GM_2v2_AT;
        fakeEvent.match.season = 0;

        fakeEvent.match.players[0].battleTag = name1;
        fakeEvent.match.players[1].battleTag = name2;
        fakeEvent.match.players[2].battleTag = name3;
        fakeEvent.match.players[3].battleTag = name4;

        return fakeEvent;
    }

    public static MatchStartedEvent Create1v1StartedEvent()
    {
        var fixture = new Fixture { RepeatCount = 2 };
        var fakeEvent = fixture.Build<MatchStartedEvent>().With(e => e.Id, ObjectId.GenerateNewId()).Create();

        var name1 = "peter#123";
        var name2 = "wolf#456";

        fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";

        fakeEvent.match.gateway = GateWay.America;
        fakeEvent.match.gameMode = GameMode.GM_1v1;
        fakeEvent.match.season = 0;

        fakeEvent.match.players[0].battleTag = name1;
        fakeEvent.match.players[1].battleTag = name2;

        return fakeEvent;
    }

    public static List<Patch> CreateFakePatches()
    {
        var fixture = new Fixture { RepeatCount = 1 };
        var fakePatch = fixture.Build<Patch>().Create();
        var fakePatch2 = fixture.Build<Patch>().Create();

        var patch1 = "1.32.5";
        var patch1_date = new DateTime(2020, 4, 29);

        var patch2 = "1.32.6";
        var patch2_date = DateTime.SpecifyKind(new DateTime(2020, 6, 2, 17, 20, 0), DateTimeKind.Utc);

        fakePatch.Version = patch1;
        fakePatch.StartDate = patch1_date;
        fakePatch2.Version = patch2;
        fakePatch2.StartDate = patch2_date;

        return new List<Patch>() { fakePatch, fakePatch2 };
    }

    public static MatchFinishedEvent CreateMatchFinishedEvent(
        string btag1,
        string btag2,
        int season,
        long startTime,
        long endTime,
        Race race1,
        Race race2,
        int mmr1 = 1000,
        int mmr2 = 1000)
    {
        var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent.match.players[0].battleTag = btag1;
        matchFinishedEvent.match.players[1].battleTag = btag2;
        matchFinishedEvent.match.players[0].race = race1;
        matchFinishedEvent.match.players[1].race = race2;
        matchFinishedEvent.match.startTime = startTime;
        matchFinishedEvent.match.endTime = endTime;
        matchFinishedEvent.match.season = season;
        matchFinishedEvent.match.players[0].mmr.rating = mmr1;
        matchFinishedEvent.match.players[1].mmr.rating = mmr2;
        return matchFinishedEvent;
    }

    public static List<Hero> CreateHeroList(IList<W3ChampionsStatisticService.Heroes.HeroType> heroes)
    {
        return heroes.Select((hero, index) => new Hero { icon = $"{Enum.GetName(hero).ToLower()}.png", level = index + 1 }).ToList();
    }

    public static Mock<ITrackingService> CreateMockTrackingService()
    {
        return new Mock<ITrackingService>();
    }

    public static Mock<TracingService> CreateMockedTracingService()
    {
        // Create a real ActivitySource for testing
        var activitySource = new ActivitySource("TestActivitySource");

        // Mock the IHttpContextAccessor
        var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();

        // Create a partial mock of TracingService - this allows you to verify calls and override specific methods
        // while keeping the real implementation for methods you don't explicitly mock
        return new Mock<TracingService>(activitySource, mockHttpContextAccessor.Object)
        {
            CallBase = true // This ensures unmocked methods use the real implementation
        };
    }

    public static Mock<MatchService> CreateMockMatchService(MongoClient mongoClient)
    {
        return new Mock<MatchService>(new Mock<IMatchRepository>().Object,
            new Mock<ICachedDataProvider<List<Matchup>>>().Object,
            new Mock<ICachedDataProvider<CachedLong>>().Object,
            new PersonalSettingsRepository(mongoClient));
    }

    public static MatchCanceledEvent CreateFakeMatchCanceledEvent()
    {
        var fixture = new Fixture { RepeatCount = 2 };
        var fakeEvent = fixture.Build<MatchCanceledEvent>().With(e => e.Id, ObjectId.GenerateNewId()).Create();

        fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";

        fakeEvent.match.gateway = GateWay.Europe;
        fakeEvent.match.gameMode = GameMode.GM_1v1;
        fakeEvent.match.season = 0;
        fakeEvent.match.id = fakeEvent.Id.ToString();
        fakeEvent.match.state = EMatchState.CANCELED;

        return fakeEvent;
    }

    public static MatchFinishedEvent CreateFakeSurvivalChaosEvent(bool enableMatchRanking = true)
    {
        var fixture = new Fixture { RepeatCount = 4 };
        fixture.Customize<PlayerMMrChange>(c => c.Without(p => p.matchRanking));
        var fakeEvent = fixture.Build<MatchFinishedEvent>().With(e => e.Id, ObjectId.GenerateNewId()).Create();

        fakeEvent.WasFakeEvent = false;
        fakeEvent.WasFromSync = false;

        var winner = "FirstPlace#123";
        var second = "SecondPlace#456";
        var third = "ThirdPlace#789";
        var fourth = "FourthPlace#000";

        fakeEvent.match.map = "Maps/frozenthrone/community/(4)survivorchaos.w3x";
        fakeEvent.match.gateway = GateWay.Europe;
        fakeEvent.match.gameMode = GameMode.FFA;
        fakeEvent.match.season = 0;

        // Each player is on their own team in survival chaos
        fakeEvent.match.players[0].battleTag = fourth;
        fakeEvent.match.players[0].team = 0;
        fakeEvent.match.players[0].won = false; // 4th place
        fakeEvent.match.players[0].race = Race.HU;
        fakeEvent.match.players[0].matchRanking = enableMatchRanking ? 3 : null; // 4th place (0-based: 3)
        fakeEvent.match.players[0].mmr = new Mmr { rating = 1500.0 };
        fakeEvent.match.players[0].updatedMmr = new Mmr { rating = 1480.0 };

        fakeEvent.match.players[1].battleTag = winner;
        fakeEvent.match.players[1].team = 1;
        fakeEvent.match.players[1].won = true; // 1st place (winner)
        fakeEvent.match.players[1].race = Race.OC;
        fakeEvent.match.players[1].matchRanking = enableMatchRanking ? 0 : null; // 1st place (0-based: 0)
        fakeEvent.match.players[1].mmr = new Mmr { rating = 1500.0 };
        fakeEvent.match.players[1].updatedMmr = new Mmr { rating = 1520.0 };

        fakeEvent.match.players[2].battleTag = third;
        fakeEvent.match.players[2].team = 2;
        fakeEvent.match.players[2].won = false; // 3rd place
        fakeEvent.match.players[2].race = Race.UD;
        fakeEvent.match.players[2].matchRanking = enableMatchRanking ? 2 : null; // 3rd place (0-based: 2)
        fakeEvent.match.players[2].mmr = new Mmr { rating = 1500.0 };
        fakeEvent.match.players[2].updatedMmr = new Mmr { rating = 1490.0 };

        fakeEvent.match.players[3].battleTag = second;
        fakeEvent.match.players[3].team = 3;
        fakeEvent.match.players[3].won = false; // 2nd place
        fakeEvent.match.players[3].race = Race.NE;
        fakeEvent.match.players[3].matchRanking = enableMatchRanking ? 1 : null; // 2nd place (0-based: 1)
        fakeEvent.match.players[3].mmr = new Mmr { rating = 1500.0 };
        fakeEvent.match.players[3].updatedMmr = new Mmr { rating = 1500.0 };

        return fakeEvent;
    }

    public static MatchFinishedEvent CreateFakeFootmenFrenzyWithTeamRanking()
    {
        var fixture = new Fixture();
        fixture.Customize<PlayerMMrChange>(c => c.Without(p => p.matchRanking));
        var fakeEvent = fixture.Build<MatchFinishedEvent>().With(e => e.Id, ObjectId.GenerateNewId()).Create();

        fakeEvent.WasFakeEvent = false;
        fakeEvent.WasFromSync = false;

        fakeEvent.match.players = new List<PlayerMMrChange>();

        // Team 0: 1st place (won) - ranking 0
        CreateMatchTeamWithRanking(fakeEvent, won: true, team: 0, playersPerTeam: 3, matchRanking: 0, "FirstPlace");
        // Team 1: 4th place (lost) - ranking 3
        CreateMatchTeamWithRanking(fakeEvent, won: false, team: 1, playersPerTeam: 3, matchRanking: 3, "FourthPlace");
        // Team 2: 2nd place (lost) - ranking 1
        CreateMatchTeamWithRanking(fakeEvent, won: false, team: 2, playersPerTeam: 3, matchRanking: 1, "SecondPlace");
        // Team 3: 3rd place (lost) - ranking 2
        CreateMatchTeamWithRanking(fakeEvent, won: false, team: 3, playersPerTeam: 3, matchRanking: 2, "ThirdPlace");

        fakeEvent.match.season = 0;
        fakeEvent.match.gameMode = GameMode.GM_FOOTMEN_FRENZY;
        fakeEvent.match.gateway = GateWay.Europe;
        fakeEvent.match.map = "W3Champions/Custom/Footmen_Frenzy_v5.8.0_W3C.w3x";

        return fakeEvent;
    }

    public static MatchFinishedEvent CreateFakeLineTowerWarsWithIndividualRanking()
    {
        var fixture = new Fixture();
        fixture.Customize<PlayerMMrChange>(c => c.Without(p => p.matchRanking));
        var fakeEvent = fixture.Build<MatchFinishedEvent>().With(e => e.Id, ObjectId.GenerateNewId()).Create();

        fakeEvent.WasFakeEvent = false;
        fakeEvent.WasFromSync = false;

        fakeEvent.match.players = new List<PlayerMMrChange>();
        fakeEvent.match.map = "W3Champions/Custom/LineTowerWars_v1.0.w3x";
        fakeEvent.match.gateway = GateWay.Europe;
        fakeEvent.match.gameMode = GameMode.GM_LTW_FFA;
        fakeEvent.match.season = 0;

        var positions = new[] { "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth" };

        // Create 8 individual players (1 player per "team") with individual rankings
        // Only the player with ranking 0 won, all others lost
        for (int i = 0; i < 8; i++)
        {
            CreateMatchTeamWithRanking(fakeEvent, won: i == 0, team: i, playersPerTeam: 1, matchRanking: i, positions[i] + "Place");
        }

        return fakeEvent;
    }

    public static MatchFinishedEvent CreateFake4v4MeleeWithoutRanking()
    {
        var fixture = new Fixture();
        fixture.Customize<PlayerMMrChange>(c => c.Without(p => p.matchRanking));
        var fakeEvent = fixture.Build<MatchFinishedEvent>().With(e => e.Id, ObjectId.GenerateNewId()).Create();

        fakeEvent.WasFakeEvent = false;
        fakeEvent.WasFromSync = false;

        fakeEvent.match.players = new List<PlayerMMrChange>();
        fakeEvent.match.map = "Maps/frozenthrone/community/(8)avalanche.w3x";
        fakeEvent.match.gateway = GateWay.Europe;
        fakeEvent.match.gameMode = GameMode.GM_4v4;
        fakeEvent.match.season = 0;

        // Team 0: winners (4 players) - no match ranking
        CreateMatchTeamWithRanking(fakeEvent, won: true, team: 0, playersPerTeam: 4, matchRanking: null, "Winners");
        // Team 1: losers (4 players) - no match ranking  
        CreateMatchTeamWithRanking(fakeEvent, won: false, team: 1, playersPerTeam: 4, matchRanking: null, "Losers");

        return fakeEvent;
    }
}
