using System;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerProfiles;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerReadmodelTests
    {
        [Test]
        public void Player_RecentProgress_Mapping()
        {
            var ev = TestDtoHelper.CreateFakeEvent();
            ev.match.players[0].id = "peter#123@10";
            ev.match.players[0].won = true;

            var player = PlayerProfile.Create("peter#12@10", "Peter#12");
            player.GameModeStats[0].RankProgressionStart = RankProgression.Create(90, 200, 3, 4, 8);
            player.GameModeStats[0].MMR = 100;
            player.GameModeStats[0].RankingPoints = 220;
            player.GameModeStats[0].Rank = 5;
            player.GameModeStats[0].LeagueId = 10;
            player.GameModeStats[0].LeagueOrder = 15;

            Assert.AreEqual(20, player.GameModeStats[0].RankingPointsProgress.RankingPoints);
            Assert.AreEqual(10, player.GameModeStats[0].RankingPointsProgress.MMR);
            Assert.AreEqual(-2, player.GameModeStats[0].RankingPointsProgress.Rank);
            Assert.AreEqual(6, player.GameModeStats[0].RankingPointsProgress.LeagueId);
            Assert.AreEqual(7, player.GameModeStats[0].RankingPointsProgress.LeagueOrder);
        }

        [Test]
        public void Player_RecentProgress_NewPlayer()
        {
            var ev = TestDtoHelper.CreateFakeEvent();
            ev.match.players[0].id = "peter#123@10";
            ev.match.players[0].won = true;

            var player = PlayerProfile.Create("peter#12@10", "Peter#12");

            player.GameModeStats[0].MMR = 100;
            player.GameModeStats[0].RankingPoints = 220;
            player.GameModeStats[0].Rank = 5;
            player.GameModeStats[0].LeagueId = 10;
            player.GameModeStats[0].LeagueOrder = 15;

            Assert.AreEqual(0, player.GameModeStats[0].RankingPointsProgress.RankingPoints);
        }

        [Test]
        public void Player_RecentProgress_After8Hours()
        {
            var ev = TestDtoHelper.CreateFakeEvent();
            ev.match.players[0].id = "peter#123@10";
            ev.match.players[0].won = true;

            var player = PlayerProfile.Create("peter#12@10", "Peter#12");
            player.GameModeStats[0].RankProgressionStart = RankProgression.Create(0, 200, 0, 0, 0);
            player.GameModeStats[0].RankProgressionStart.Date = DateTimeOffset.UtcNow.AddDays(-1);

            player.GameModeStats[0].MMR = 100;
            player.GameModeStats[0].RankingPoints = 220;
            player.GameModeStats[0].Rank = 5;
            player.GameModeStats[0].LeagueId = 10;
            player.GameModeStats[0].LeagueOrder = 15;

            Assert.AreEqual(0, player.GameModeStats[0].RankingPointsProgress.RankingPoints);
        }

        [Test]
        [TestCase(1, 1, 0)]
        [TestCase(0, 0, 0)]
        [TestCase(3, 2, 1)]
        [TestCase(7, 2, 5)]
        public void GameModeStat_LatestDivision(int league, int order, int division)
        {
            var gameModeStat = new GameModeStat(GameMode.GM_1v1);

            gameModeStat.LeagueId = league;
            gameModeStat.LeagueOrder = order;

            Assert.AreEqual(division, gameModeStat.Division);
        }
    }
}