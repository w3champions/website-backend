using NUnit.Framework;
using W3ChampionsStatisticService.Hubs;

namespace WC3ChampionsStatisticService.Tests.Hubs;

[TestFixture]
public class ConnectionMappingSnapshotTests
{
    [Test]
    public void GetConnectionId_ReturnsACopy_ThatLaterRemovalsDoNotMutate()
    {
        var mapping = new ConnectionMapping();
        mapping.Add("c1", new WebSocketUser { BattleTag = "A#1", ConnectionId = "c1" });
        mapping.Add("c2", new WebSocketUser { BattleTag = "A#1", ConnectionId = "c2" });

        var ids = mapping.GetConnectionId("A#1");
        mapping.Remove("c1");

        Assert.That(ids, Is.EquivalentTo(new[] { "c1", "c2" }));
        Assert.That(mapping.GetConnectionId("A#1"), Is.EquivalentTo(new[] { "c2" }));
    }

    [Test]
    public void GetOnlineBattleTags_ReturnsDistinctSortedTags()
    {
        var mapping = new ConnectionMapping();
        mapping.Add("c1", new WebSocketUser { BattleTag = "B#2", ConnectionId = "c1" });
        mapping.Add("c2", new WebSocketUser { BattleTag = "A#1", ConnectionId = "c2" });
        mapping.Add("c3", new WebSocketUser { BattleTag = "B#2", ConnectionId = "c3" });

        Assert.That(mapping.GetOnlineBattleTags(), Is.EqualTo(new[] { "A#1", "B#2" }));
    }
}
