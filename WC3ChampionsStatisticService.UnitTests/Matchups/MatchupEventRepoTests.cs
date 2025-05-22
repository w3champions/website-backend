using MongoDB.Bson;
using NUnit.Framework;
using System;
using System.Threading.Tasks;
using W3C.Domain.Repositories;

namespace WC3ChampionsStatisticService.Tests.Matchups;

[TestFixture]
public class MatchupEventRepoTests : IntegrationTestBase
{
    [Test]
    public async Task LoadAndSave()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();

        matchFinishedEvent1.match.id = "nmhcCLaRc7";

        await InsertMatchEvent(matchFinishedEvent1);

        var matchEventRepository = new MatchEventRepository(MongoClient);

        await matchEventRepository.InsertIfNotExisting(matchFinishedEvent1);

        var events = await matchEventRepository.Load();

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(false, events[0].WasFromSync);
    }

    [Test]
    public async Task LoadAndSave2()
    {
        var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
        var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();

        matchFinishedEvent1.match.id = "nmhcCLaRc7";
        matchFinishedEvent2.match.id = "ashjkn75j4";

        await InsertMatchEvent(matchFinishedEvent1);

        var matchEventRepository = new MatchEventRepository(MongoClient);

        await matchEventRepository.InsertIfNotExisting(matchFinishedEvent1);
        await matchEventRepository.InsertIfNotExisting(matchFinishedEvent2);

        var events = await matchEventRepository.Load();

        Assert.AreEqual(2, events.Count);
        Assert.AreEqual(false, events[0].WasFromSync);
        Assert.AreEqual(true, events[1].WasFromSync);
    }

    [Test]
    public async Task LoadStartedIgnoresEventThatAreYoungerThan20Seconds()
    {
        var startEvent1 = TestDtoHelper.CreateFakeStartedEvent();
        var startEvent2 = TestDtoHelper.CreateFakeStartedEvent();

        startEvent1.Id = ObjectId.GenerateNewId(DateTime.Now.AddDays(-1));
        startEvent1.match.id = "test";
        startEvent2.Id = ObjectId.GenerateNewId(DateTime.Now);

        await InsertMatchStartedEvent(startEvent1);
        await InsertMatchStartedEvent(startEvent2);

        var matchEventRepository = new MatchEventRepository(MongoClient);

        var events = await matchEventRepository.LoadStartedMatches();

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(startEvent1.match.id, events[0].match.id);
    }
}
