using System;
using NUnit.Framework;
using W3ChampionsStatisticService;

namespace WC3ChampionsStatisticService.Tests;

[TestFixture]
public class MongoClientSettingsFactoryTests
{
    [Test]
    public void Create_FailsFastInsteadOfQueueingForTheDriverDefaultTwoMinutes()
    {
        var settings = MongoClientSettingsFactory.Create("mongodb://user:pw@db-website-backend:27017");

        Assert.That(settings.WaitQueueTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
        Assert.That(settings.ServerSelectionTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(settings.ConnectTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void Create_KeepsTheConnectionStringTarget()
    {
        var settings = MongoClientSettingsFactory.Create("mongodb://user:pw@db-website-backend:27017");

        Assert.That(settings.Server.Host, Is.EqualTo("db-website-backend"));
        Assert.That(settings.Server.Port, Is.EqualTo(27017));
    }
}
