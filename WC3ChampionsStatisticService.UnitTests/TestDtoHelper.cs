using System;
using System.Collections.Generic;
using System.Linq;
using AutoFixture;
using MongoDB.Bson;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PadEvents;

namespace WC3ChampionsStatisticService.UnitTests
{
    public static class TestDtoHelper
    {
        public static MatchFinishedEvent CreateFakeEvent()
        {
            var fixture = new Fixture { RepeatCount = 2 };
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
            for (int i = 0; i < playersPerTeam; i++)
            {
                var player = new PlayerMMrChange()
                {
                    battleTag = $"{team}#{i}",
                    team = team,
                    won = won
                };

                fakeEvent.match.players.Add(player);
            }
        }

        public static MatchFinishedEvent CreateFake2v2RTEvent()
        {
            var fixture = new Fixture { RepeatCount = 4 };
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
            var fixture = new Fixture {RepeatCount = 2};
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
    }
}