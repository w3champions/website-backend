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
            player.GameModeStats[0].RankProgressionStart = RankProgression.Create(90, 200);
            player.UpdateRank(GameMode.GM_1v1, 100, 220);

            Assert.AreEqual(20, player.GameModeStats[0].RankingPointsProgress.RankingPoints);
            Assert.AreEqual(10, player.GameModeStats[0].RankingPointsProgress.MMR);
        }

        [Test]
        public void Player_RecentProgress_DoubleUpdate()
        {
            var ev = TestDtoHelper.CreateFakeEvent();
            ev.match.players[0].id = "peter#123@10";
            ev.match.players[0].won = true;

            var player = PlayerProfile.Create("peter#12@10", "Peter#12");
            player.GameModeStats[0].RankProgressionStart = RankProgression.Create(0, 200);
            player.UpdateRank(GameMode.GM_1v1, 100, 220);
            player.UpdateRank(GameMode.GM_1v1, 100, 230);

            Assert.AreEqual(30, player.GameModeStats[0].RankingPointsProgress.RankingPoints);
        }

        [Test]
        public void Player_RecentProgress_DoubleUpdate_NoChange()
        {
            var ev = TestDtoHelper.CreateFakeEvent();
            ev.match.players[0].id = "peter#123@10";
            ev.match.players[0].won = true;

            var player = PlayerProfile.Create("peter#12@10", "Peter#12");
            player.GameModeStats[0].RankProgressionStart = RankProgression.Create(0, 200);
            player.UpdateRank(GameMode.GM_1v1, 100, 220);
            player.UpdateRank(GameMode.GM_1v1, 100, 200);

            Assert.AreEqual(0, player.GameModeStats[0].RankingPointsProgress.RankingPoints);
        }

        [Test]
        public void Player_RecentProgress_DoubleUpdate_NegativeThenPositive()
        {
            var ev = TestDtoHelper.CreateFakeEvent();
            ev.match.players[0].id = "peter#123@10";
            ev.match.players[0].won = true;

            var player = PlayerProfile.Create("peter#12@10", "Peter#12");
            player.GameModeStats[0].RankProgressionStart = RankProgression.Create(0, 200);
            player.UpdateRank(GameMode.GM_1v1, 100, 180);
            player.UpdateRank(GameMode.GM_1v1, 100, 230);

            Assert.AreEqual(30, player.GameModeStats[0].RankingPointsProgress.RankingPoints);
        }

        [Test]
        public void Player_RecentProgress_NewPlayer()
        {
            var ev = TestDtoHelper.CreateFakeEvent();
            ev.match.players[0].id = "peter#123@10";
            ev.match.players[0].won = true;

            var player = PlayerProfile.Create("peter#12@10", "Peter#12");
            player.UpdateRank(GameMode.GM_1v1, 100, 180);
            player.UpdateRank(GameMode.GM_1v1, 100, 230);

            Assert.AreEqual(50, player.GameModeStats[0].RankingPointsProgress.RankingPoints);
        }

        [Test]
        public void Player_RecentProgress_After8Hours()
        {
            var ev = TestDtoHelper.CreateFakeEvent();
            ev.match.players[0].id = "peter#123@10";
            ev.match.players[0].won = true;

            var player = PlayerProfile.Create("peter#12@10", "Peter#12");
            player.GameModeStats[0].RankProgressionStart = RankProgression.Create(0, 200);
            player.GameModeStats[0].RankProgressionStart.Date = DateTimeOffset.UtcNow.AddDays(-1);
            player.UpdateRank(GameMode.GM_1v1, 100, 180);

            Assert.AreEqual(0, player.GameModeStats[0].RankingPointsProgress.RankingPoints);
        }
    }
}