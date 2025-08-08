using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Heroes;
using W3ChampionsStatisticService.Matches;

namespace WC3ChampionsStatisticService.Tests.Matchups;

[TestFixture]
public class MatchupTests
{
    [Test]
    public void MapMatch_Players()
    {
        var fakeEvent = TestDtoHelper.CreateFakeEvent();

        var name1 = "peter#123";
        var name2 = "wolf#456";

        fakeEvent.match.players.First().battleTag = name1;
        fakeEvent.match.players.First().won = false;
        fakeEvent.match.players.Last().battleTag = name2;
        fakeEvent.match.players.Last().won = true;

        var matchup = Matchup.Create(fakeEvent);

        Assert.AreEqual("wolf#456", matchup.Teams.First().Players.First().BattleTag);
        Assert.AreEqual("wolf#456", matchup.Team1Players);
        Assert.AreEqual("wolf", matchup.Teams.First().Players.First().Name);

        Assert.AreEqual("peter#123", matchup.Teams.Last().Players.First().BattleTag);
        Assert.AreEqual("peter#123", matchup.Team2Players);
        Assert.AreEqual("peter", matchup.Teams.Last().Players.First().Name);
    }

    [Test]
    public void MapMatch_Map()
    {
        var fakeEvent = TestDtoHelper.CreateFakeEvent();
        fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";
        var matchup = Matchup.Create(fakeEvent);
        Assert.AreEqual("amazonia", matchup.Map);
    }

    [Test]
    public void MapMatch_Map_Shorter()
    {
        var fakeEvent = TestDtoHelper.CreateFakeEvent();
        fakeEvent.match.map = "Maps/frozenthrone/(2)terenasstand_lv.w3x";
        var matchup = Matchup.Create(fakeEvent);
        Assert.AreEqual("terenasstand", matchup.Map);
    }

    [Test]
    public void MapMatch_TimeSpan()
    {
        var fakeEvent = TestDtoHelper.CreateFakeEvent();
        fakeEvent.match.startTime = 1585692028740;
        fakeEvent.match.endTime = 1585692047363;
        var matchup = Matchup.Create(fakeEvent);
        Assert.AreEqual(0, matchup.Duration.Minutes);
        Assert.AreEqual(18, matchup.Duration.Seconds);
        Assert.AreEqual(623, matchup.Duration.Milliseconds);
    }

    [Test]
    public void MapMatch_MMr()
    {
        var fakeEvent = TestDtoHelper.CreateFakeEvent();
        fakeEvent.match.players[0].won = true;
        fakeEvent.match.players[0].mmr.rating = 1437.0358093886573;
        fakeEvent.match.players[0].updatedMmr.rating = 1453.5974731933813;

        fakeEvent.match.players[1].won = false;
        fakeEvent.match.players[1].mmr.rating = 1453.5974731933813;
        fakeEvent.match.players[1].updatedMmr.rating = 1437.0358093886573;
        var matchup = Matchup.Create(fakeEvent);
        Assert.AreEqual(16, matchup.Teams[0].Players[0].MmrGain);
        Assert.AreEqual(1453, matchup.Teams[0].Players[0].CurrentMmr);
        Assert.AreEqual(1437, matchup.Teams[0].Players[0].OldMmr);
        Assert.AreEqual(-16, matchup.Teams[1].Players[0].MmrGain);
        Assert.AreEqual(1437, matchup.Teams[1].Players[0].CurrentMmr);
        Assert.AreEqual(1453, matchup.Teams[1].Players[0].OldMmr);
    }

    [Test]
    public void MapMatch_StartTime()
    {
        var fakeEvent = TestDtoHelper.CreateFakeEvent();
        var matchup = Matchup.Create(fakeEvent);
        fakeEvent.match.startTime = 1585692028740;
        fakeEvent.match.endTime = 1585692047363;
        Assert.IsNotNull(matchup.StartTime);
        Assert.IsNotNull(matchup.EndTime);
    }

    [Test]
    public void MapMatch_GameMode()
    {
        var fakeEvent = TestDtoHelper.CreateFakeEvent();
        fakeEvent.match.gameMode = GameMode.GM_1v1;
        var matchup = Matchup.Create(fakeEvent);
        Assert.AreEqual(GameMode.GM_1v1, matchup.GameMode);
    }

    [Test]
    public void MapResult_Heroes()
    {
        var fakeEvent = TestDtoHelper.CreateFakeEvent();
        fakeEvent.result.players[0].heroes = TestDtoHelper.CreateHeroList(new List<HeroType> { HeroType.Archmage });
        fakeEvent.result.players[1].heroes = TestDtoHelper.CreateHeroList(new List<HeroType> { HeroType.Farseer, HeroType.Blademaster });

        var matchup = Matchup.Create(fakeEvent);
        var firstPlayer = matchup.Teams[0].Players[0];
        Assert.AreEqual(1, firstPlayer.Heroes.Count);
        Assert.AreEqual(HeroType.Archmage, firstPlayer.Heroes[0].Id);

        var secondPlayer = matchup.Teams[1].Players[0];
        Assert.AreEqual(2, secondPlayer.Heroes.Count);
        Assert.AreEqual(HeroType.Farseer, secondPlayer.Heroes[0].Id);
        Assert.AreEqual(HeroType.Blademaster, secondPlayer.Heroes[1].Id);
    }

    [TestCase(true, Description = "Test survival chaos with match ranking enabled")]
    [TestCase(false, Description = "Test survival chaos without match ranking")]
    public void MapMatch_SurvivalChaos_WithAndWithoutMatchRanking(bool enableMatchRanking)
    {
        var fakeEvent = TestDtoHelper.CreateFakeSurvivalChaosEvent(enableMatchRanking);
        var matchup = Matchup.Create(fakeEvent);

        Assert.AreEqual(4, matchup.Teams.Count);

        if (enableMatchRanking)
        {
            // With match ranking: ordered by ranking (0, 1, 2, 3)
            Assert.AreEqual("FirstPlace#123", matchup.Teams[0].Players[0].BattleTag); // Rank 0 (winner)
            Assert.AreEqual("SecondPlace#456", matchup.Teams[1].Players[0].BattleTag); // Rank 1
            Assert.AreEqual("ThirdPlace#789", matchup.Teams[2].Players[0].BattleTag); // Rank 2  
            Assert.AreEqual("FourthPlace#000", matchup.Teams[3].Players[0].BattleTag); // Rank 3

            Assert.AreEqual(0, matchup.Teams[0].Players[0].MatchRanking);
            Assert.AreEqual(1, matchup.Teams[1].Players[0].MatchRanking);
            Assert.AreEqual(2, matchup.Teams[2].Players[0].MatchRanking);
            Assert.AreEqual(3, matchup.Teams[3].Players[0].MatchRanking);
        }
        else
        {
            // Without match ranking: ordered by won status first (winner), then by team number
            Assert.AreEqual("FirstPlace#123", matchup.Teams[0].Players[0].BattleTag); // Won (team 1)
            Assert.AreEqual("FourthPlace#000", matchup.Teams[1].Players[0].BattleTag); // Lost (team 0)
            Assert.AreEqual("ThirdPlace#789", matchup.Teams[2].Players[0].BattleTag); // Lost (team 2)
            Assert.AreEqual("SecondPlace#456", matchup.Teams[3].Players[0].BattleTag); // Lost (team 3)

            Assert.IsTrue(matchup.Teams[0].Players[0].Won);
            Assert.IsFalse(matchup.Teams[1].Players[0].Won);
            Assert.IsFalse(matchup.Teams[2].Players[0].Won);
            Assert.IsFalse(matchup.Teams[3].Players[0].Won);

            foreach (var team in matchup.Teams)
            {
                Assert.IsNull(team.Players[0].MatchRanking);
            }
        }
    }

    [Test]
    public void MapMatch_FootmenFrenzy_TeamMatchRanking()
    {
        var fakeEvent = TestDtoHelper.CreateFakeFootmenFrenzyWithTeamRanking();
        var matchup = Matchup.Create(fakeEvent);

        Assert.AreEqual(4, matchup.Teams.Count);

        // Teams should be ordered by match ranking (0, 1, 2, 3)
        Assert.AreEqual(0, matchup.Teams[0].MatchRanking); // 1st place team (won)
        Assert.AreEqual(1, matchup.Teams[1].MatchRanking); // 2nd place team
        Assert.AreEqual(2, matchup.Teams[2].MatchRanking); // 3rd place team
        Assert.AreEqual(3, matchup.Teams[3].MatchRanking); // 4th place team

        // Verify battle tags for team ordering
        Assert.AreEqual("FirstPlacePlayer1#00", matchup.Teams[0].Players[0].BattleTag); // Team 0 (rank 0)
        Assert.AreEqual("SecondPlacePlayer1#20", matchup.Teams[1].Players[0].BattleTag); // Team 2 (rank 1)
        Assert.AreEqual("ThirdPlacePlayer1#30", matchup.Teams[2].Players[0].BattleTag); // Team 3 (rank 2)
        Assert.AreEqual("FourthPlacePlayer1#10", matchup.Teams[3].Players[0].BattleTag); // Team 1 (rank 3)

        // Verify team 0 won, others lost
        Assert.IsTrue(matchup.Teams[0].Won);
        Assert.IsFalse(matchup.Teams[1].Won);
        Assert.IsFalse(matchup.Teams[2].Won);
        Assert.IsFalse(matchup.Teams[3].Won);

        // Each team should have 3 players with the same match ranking
        foreach (var team in matchup.Teams)
        {
            Assert.AreEqual(3, team.Players.Count);
            var teamRanking = team.MatchRanking;
            foreach (var player in team.Players)
            {
                Assert.AreEqual(teamRanking, player.MatchRanking);
            }
        }
    }

    [Test]
    public void MapMatch_LineTowerWars_IndividualMatchRanking()
    {
        var fakeEvent = TestDtoHelper.CreateFakeLineTowerWarsWithIndividualRanking();
        var matchup = Matchup.Create(fakeEvent);

        Assert.AreEqual(8, matchup.Teams.Count);

        // Verify battle tags are ordered by individual match ranking (0-7)
        Assert.AreEqual("FirstPlacePlayer1#00", matchup.Teams[0].Players[0].BattleTag); // Rank 0 (winner)
        Assert.AreEqual("SecondPlacePlayer1#10", matchup.Teams[1].Players[0].BattleTag); // Rank 1
        Assert.AreEqual("ThirdPlacePlayer1#20", matchup.Teams[2].Players[0].BattleTag); // Rank 2
        Assert.AreEqual("FourthPlacePlayer1#30", matchup.Teams[3].Players[0].BattleTag); // Rank 3
        Assert.AreEqual("FifthPlacePlayer1#40", matchup.Teams[4].Players[0].BattleTag); // Rank 4
        Assert.AreEqual("SixthPlacePlayer1#50", matchup.Teams[5].Players[0].BattleTag); // Rank 5
        Assert.AreEqual("SeventhPlacePlayer1#60", matchup.Teams[6].Players[0].BattleTag); // Rank 6
        Assert.AreEqual("EighthPlacePlayer1#70", matchup.Teams[7].Players[0].BattleTag); // Rank 7

        // Verify match rankings
        for (int i = 0; i < 8; i++)
        {
            Assert.AreEqual(i, matchup.Teams[i].Players[0].MatchRanking);
            Assert.AreEqual(1, matchup.Teams[i].Players.Count); // Each team has 1 player
        }

        // Only first player (ranking 0) should have won
        Assert.IsTrue(matchup.Teams[0].Players[0].Won);
        for (int i = 1; i < 8; i++)
        {
            Assert.IsFalse(matchup.Teams[i].Players[0].Won);
        }
    }

    [Test]
    public void MapMatch_4v4Melee_WithoutMatchRanking()
    {
        var fakeEvent = TestDtoHelper.CreateFake4v4MeleeWithoutRanking();
        var matchup = Matchup.Create(fakeEvent);

        Assert.AreEqual(2, matchup.Teams.Count);

        // Without match ranking: ordered by won status, winning team first
        // Verify battle tags for team ordering
        Assert.AreEqual("WinnersPlayer1#00", matchup.Teams[0].Players[0].BattleTag); // Team 0 (winners)
        Assert.AreEqual("LosersPlayer1#10", matchup.Teams[1].Players[0].BattleTag); // Team 1 (losers)

        Assert.IsTrue(matchup.Teams[0].Won);
        Assert.IsFalse(matchup.Teams[1].Won);

        // Each team should have 4 players with no match ranking
        foreach (var team in matchup.Teams)
        {
            Assert.AreEqual(4, team.Players.Count);
            Assert.IsNull(team.MatchRanking);
            foreach (var player in team.Players)
            {
                Assert.IsNull(player.MatchRanking);
            }
        }
    }
}
