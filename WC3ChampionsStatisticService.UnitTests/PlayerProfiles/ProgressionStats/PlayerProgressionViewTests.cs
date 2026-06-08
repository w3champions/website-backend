using NUnit.Framework;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.ProgressionStats;

[TestFixture]
public class PlayerProgressionViewTests
{
    [Test]
    public void FromReadModel_Null_ReturnsNull()
    {
        Assert.IsNull(PlayerProgressionView.FromReadModel(null));
    }

    [Test]
    public void FromReadModel_MapsTheFourPublishedFields()
    {
        var p = new PlayerProgression { League = 3, Division = 2, Points = 50, ApexPoints = 120 };
        var view = PlayerProgressionView.FromReadModel(p);
        Assert.AreEqual(3, view.League);
        Assert.AreEqual(2, view.Division);
        Assert.AreEqual(50, view.Points);
        Assert.AreEqual(120, view.ApexPoints);
    }
}
