using System;
using System.Collections.Generic;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerReadmodelTests
    {
        [Test]
        public void Player_RecentProgress_Mapping()
        {
            var btag = new BattleTagIdCombined(new List<PlayerId>
                {
                    PlayerId.Create("Peter#12")
                },
                GateWay.America,
                GameMode.GM_1v1,
                0);
            var gameModeStatPerGateway = PlayerGameModeStatPerGateway.Create(btag);
            gameModeStatPerGateway.RankProgressionStart = RankProgression.Create(90, 200);
            gameModeStatPerGateway.RecordRanking(100, 220);

            Assert.AreEqual(20, gameModeStatPerGateway.RankingPointsProgress.RankingPoints);
            Assert.AreEqual(10, gameModeStatPerGateway.RankingPointsProgress.MMR);
        }

        [Test]
        public void Player_RecentProgress_DoubleUpdate()
        {
            var btag = new BattleTagIdCombined(new List<PlayerId>
                {
                    PlayerId.Create("Peter#12")
                },
                GateWay.America,
                GameMode.GM_1v1,
                0);
            var gameModeStatPerGateway = PlayerGameModeStatPerGateway.Create(btag);

            gameModeStatPerGateway.RankProgressionStart = RankProgression.Create(0, 200);
            gameModeStatPerGateway.RecordRanking(100, 220);
            gameModeStatPerGateway.RecordRanking(100, 230);

            Assert.AreEqual(30, gameModeStatPerGateway.RankingPointsProgress.RankingPoints);
        }

        [Test]
        public void Player_RecentProgress_DoubleUpdate_NoChange()
        {
            var btag = new BattleTagIdCombined(new List<PlayerId>
                {
                    PlayerId.Create("Peter#12")
                },
                GateWay.America,
                GameMode.GM_1v1,
                0);
            var gameModeStatPerGateway = PlayerGameModeStatPerGateway.Create(btag);

            gameModeStatPerGateway.RankProgressionStart = RankProgression.Create(0, 200);
            gameModeStatPerGateway.RecordRanking(100, 220);
            gameModeStatPerGateway.RecordRanking(100, 200);

            Assert.AreEqual(0, gameModeStatPerGateway.RankingPointsProgress.RankingPoints);
        }

        [Test]
        public void Player_RecentProgress_DoubleUpdate_NegativeThenPositive()
        {
            var btag = new BattleTagIdCombined(new List<PlayerId>
                {
                    PlayerId.Create("Peter#12")
                },
                GateWay.America,
                GameMode.GM_1v1,
                0);
            var gameModeStatPerGateway = PlayerGameModeStatPerGateway.Create(btag);

            gameModeStatPerGateway.RankProgressionStart = RankProgression.Create(0, 200);
            gameModeStatPerGateway.RecordRanking(100, 180);
            gameModeStatPerGateway.RecordRanking(100, 230);

            Assert.AreEqual(30, gameModeStatPerGateway.RankingPointsProgress.RankingPoints);
        }

        [Test]
        public void Player_RecentProgress_NewPlayer()
        {
            var btag = new BattleTagIdCombined(new List<PlayerId>
                {
                    PlayerId.Create("Peter#12")
                },
                GateWay.America,
                GameMode.GM_1v1,
                0);
            var gameModeStatPerGateway = PlayerGameModeStatPerGateway.Create(btag);

            gameModeStatPerGateway.RecordRanking(100, 180);
            gameModeStatPerGateway.RecordRanking(100, 230);

            Assert.AreEqual(50, gameModeStatPerGateway.RankingPointsProgress.RankingPoints);
        }

        [Test]
        public void Player_RecentProgress_After8Hours()
        {
            var btag = new BattleTagIdCombined(new List<PlayerId>
                {
                    PlayerId.Create("Peter#12")
                },
                GateWay.America,
                GameMode.GM_1v1,
                0);
            var gameModeStatPerGateway = PlayerGameModeStatPerGateway.Create(btag);

            gameModeStatPerGateway.RankProgressionStart = RankProgression.Create(0, 200);
            gameModeStatPerGateway.RecordRanking(100, 180);
            gameModeStatPerGateway.RankProgressionStart.Date = DateTimeOffset.UtcNow.AddDays(-1);
            gameModeStatPerGateway.RecordRanking(100, 230);

            Assert.AreEqual(0, gameModeStatPerGateway.RankingPointsProgress.RankingPoints);
        }
    }
}