using System.Collections.Generic;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.ProgressionStats;

[TestFixture]
public class PlayerProgressionTests
{
    [Test]
    public void Create_FromCombinedId_CopiesIdentityFields()
    {
        var id = new BattleTagIdCombined(
            new List<PlayerId> { PlayerId.Create("peter#123") },
            GateWay.Europe, GameMode.GM_1v1, 2, Race.HU);

        var progression = PlayerProgression.Create(id);

        Assert.AreEqual(id.Id, progression.Id);
        Assert.AreEqual(2, progression.Season);
        Assert.AreEqual(GateWay.Europe, progression.GateWay);
        Assert.AreEqual(GameMode.GM_1v1, progression.GameMode);
        Assert.AreEqual(Race.HU, progression.Race);
        Assert.AreEqual(1, progression.PlayerIds.Count);
        Assert.AreEqual("peter#123", progression.PlayerIds[0].BattleTag);
    }

    [Test]
    public void Create_FromAtTeamId_HasNullRace()
    {
        var id = new BattleTagIdCombined(
            new List<PlayerId> { PlayerId.Create("alice#1"), PlayerId.Create("bob#2") },
            GateWay.Europe, GameMode.GM_2v2, 2, null);

        var progression = PlayerProgression.Create(id);

        Assert.IsNull(progression.Race);
        Assert.AreEqual(2, progression.PlayerIds.Count);
        Assert.AreEqual(id.Id, progression.Id);
    }

    [Test]
    public void RecordRank_SetsRankFields()
    {
        var id = new BattleTagIdCombined(
            new List<PlayerId> { PlayerId.Create("peter#123") },
            GateWay.Europe, GameMode.GM_1v1, 2, Race.HU);
        var progression = PlayerProgression.Create(id);

        progression.RecordRank(3, 2, 50, null);

        Assert.AreEqual(3, progression.League);
        Assert.AreEqual(2, progression.Division);
        Assert.AreEqual(50, progression.Points);
        Assert.IsNull(progression.ApexPoints);
    }

    [Test]
    public void RecordRank_OverwritesPreviousRank()
    {
        var id = new BattleTagIdCombined(
            new List<PlayerId> { PlayerId.Create("peter#123") },
            GateWay.Europe, GameMode.GM_1v1, 2, Race.HU);
        var progression = PlayerProgression.Create(id);
        progression.RecordRank(1, 0, 80, null);

        progression.RecordRank(3, 2, 50, 10);

        Assert.AreEqual(3, progression.League);
        Assert.AreEqual(2, progression.Division);
        Assert.AreEqual(50, progression.Points);
        Assert.AreEqual(10, progression.ApexPoints);
    }
}
