using System;
using NUnit.Framework;
using W3ChampionsStatisticService;

namespace WC3ChampionsStatisticService.Tests;

[TestFixture]
public class MongoConnectionStringResolverTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("''")]
    public void Resolve_Throws_WhenMissingOrEmpty(string raw)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => MongoConnectionStringResolver.Resolve(raw));
        StringAssert.Contains("MONGO_CONNECTION_STRING", ex.Message);
    }

    [Test]
    public void Resolve_ReturnsValue_AndStripsSingleQuotes()
    {
        Assert.AreEqual("mongodb://user:pw@host:27017", MongoConnectionStringResolver.Resolve("'mongodb://user:pw@host:27017'"));
    }
}
