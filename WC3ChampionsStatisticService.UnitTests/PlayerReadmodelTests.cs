using System;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles;

namespace WC3ChampionsStatisticService.UnitTests
{
    // [TestFixture]
    // public class PlayerReadmodelTests
    // {
    //     [Test]
    //     public void Player_RecentProgress_Mapping()
    //     {
    //         var ev = TestDtoHelper.CreateFakeEvent();
    //         ev.match.players[0].battleTag = "peter#123";
    //         ev.match.players[0].won = true;
    //         ev.match.gateway = GateWay.America;
    //
    //         var player = PlayerProfile.Create("Peter#12");
    //         player.GateWayStats.Add(GameModeStatsPerGateway.Create(GateWay.Europe, 0));
    //         player.GetStatForGateway(GateWay.Europe).GameModeStats[0].RankProgressionStart = RankProgression.Create(90, 200);
    //         player.UpdateRank(GameMode.GM_1v1, GateWay.Europe, 100, 220, 0);
    //
    //         Assert.AreEqual(20, player.GetStatForGateway(GateWay.Europe).GameModeStats[0].RankingPointsProgress.RankingPoints);
    //         Assert.AreEqual(10, player.GetStatForGateway(GateWay.Europe).GameModeStats[0].RankingPointsProgress.MMR);
    //     }
    //
    //     [Test]
    //     public void Player_RecentProgress_DoubleUpdate()
    //     {
    //         var ev = TestDtoHelper.CreateFakeEvent();
    //         ev.match.players[0].battleTag = "peter#123";
    //         ev.match.players[0].won = true;
    //         ev.match.gateway = GateWay.America;
    //
    //         var player = PlayerProfile.Create("Peter#12");
    //         player.GateWayStats.Add(GameModeStatsPerGateway.Create(GateWay.Europe, 0));
    //         player.GetStatForGateway(GateWay.Europe).GameModeStats[0].RankProgressionStart = RankProgression.Create(0, 200);
    //         player.UpdateRank(GameMode.GM_1v1, GateWay.Europe, 100, 220, 0);
    //         player.UpdateRank(GameMode.GM_1v1, GateWay.Europe, 100, 230, 0);
    //
    //         Assert.AreEqual(30, player.GetStatForGateway(GateWay.Europe).GameModeStats[0].RankingPointsProgress.RankingPoints);
    //     }
    //
    //     [Test]
    //     public void Player_RecentProgress_DoubleUpdate_NoChange()
    //     {
    //         var ev = TestDtoHelper.CreateFakeEvent();
    //         ev.match.players[0].battleTag = "peter#123";
    //         ev.match.players[0].won = true;
    //         ev.match.gateway = GateWay.America;
    //
    //         var player = PlayerProfile.Create("Peter#12");
    //         player.GateWayStats.Add(GameModeStatsPerGateway.Create(GateWay.Europe, 0));
    //         player.GetStatForGateway(GateWay.Europe).GameModeStats[0].RankProgressionStart = RankProgression.Create(0, 200);
    //         player.UpdateRank(GameMode.GM_1v1, GateWay.Europe, 100, 220, 0);
    //         player.UpdateRank(GameMode.GM_1v1, GateWay.Europe, 100, 200, 0);
    //
    //         Assert.AreEqual(0, player.GetStatForGateway(GateWay.Europe).GameModeStats[0].RankingPointsProgress.RankingPoints);
    //     }
    //
    //     [Test]
    //     public void Player_RecentProgress_DoubleUpdate_NegativeThenPositive()
    //     {
    //         var ev = TestDtoHelper.CreateFakeEvent();
    //         ev.match.players[0].battleTag = "peter#123";
    //         ev.match.players[0].won = true;
    //         ev.match.gateway = GateWay.America;
    //
    //         var player = PlayerProfile.Create("Peter#12");
    //         player.GateWayStats.Add(GameModeStatsPerGateway.Create(GateWay.Europe, 0));
    //         player.GetStatForGateway(GateWay.Europe).GameModeStats[0].RankProgressionStart = RankProgression.Create(0, 200);
    //         player.UpdateRank(GameMode.GM_1v1, GateWay.Europe, 100, 180, 0);
    //         player.UpdateRank(GameMode.GM_1v1, GateWay.Europe, 100, 230, 0);
    //
    //         Assert.AreEqual(30, player.GetStatForGateway(GateWay.Europe).GameModeStats[0].RankingPointsProgress.RankingPoints);
    //     }
    //
    //     [Test]
    //     public void Player_RecentProgress_NewPlayer()
    //     {
    //         var ev = TestDtoHelper.CreateFakeEvent();
    //         ev.match.players[0].battleTag = "peter#123";
    //         ev.match.players[0].won = true;
    //         ev.match.gateway = GateWay.America;
    //
    //         var player = PlayerProfile.Create("Peter#12");
    //         player.UpdateRank(GameMode.GM_1v1, GateWay.Europe, 100, 180, 0);
    //         player.UpdateRank(GameMode.GM_1v1, GateWay.Europe, 100, 230, 0);
    //
    //         Assert.AreEqual(50, player.GetStatForGateway(GateWay.Europe).GameModeStats[0].RankingPointsProgress.RankingPoints);
    //     }
    //
    //     [Test]
    //     public void Player_RecentProgress_After8Hours()
    //     {
    //         var ev = TestDtoHelper.CreateFakeEvent();
    //         ev.match.players[0].battleTag = "peter#123";
    //         ev.match.players[0].won = true;
    //         ev.match.gateway = GateWay.America;
    //
    //         var player = PlayerProfile.Create("Peter#12");
    //         player.GateWayStats.Add(GameModeStatsPerGateway.Create(GateWay.Europe, 0));
    //         player.GateWayStats[0].GameModeStats[0].RankProgressionStart = RankProgression.Create(0, 200);
    //         player.GateWayStats[0].GameModeStats[0].RankProgressionStart.Date = DateTimeOffset.UtcNow.AddDays(-1);
    //         player.UpdateRank(GameMode.GM_1v1, GateWay.Europe, 100, 180, 0);
    //
    //         Assert.AreEqual(0, player.GetStatForGateway(GateWay.Europe).GameModeStats[0].RankingPointsProgress.RankingPoints);
    //     }
    // }
}