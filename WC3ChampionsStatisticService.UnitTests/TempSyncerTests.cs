using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class TempSyncerTests : IntegrationTestBase
    {
        [Test]
        public async Task TempSyncHappyPath()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();
            var fakeEvent2 = TestDtoHelper.CreateFakeEvent();

            fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";
            fakeEvent.match.state = 2;
            var mockEvents = new Mock<IMatchEventRepository>();
            mockEvents.SetupSequence(m => m.Load(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new List<MatchFinishedEvent>() { fakeEvent, fakeEvent2 })
                .ReturnsAsync(new List<MatchFinishedEvent>());

            var mockMatchRepo = new MatchRepository(MongoClient);

            var serviceCollection = new ServiceCollection();
            var provider = serviceCollection.BuildServiceProvider();
            var mock = new Mock<IServiceScopeFactory>();
            mock.Setup(m => m.CreateScope()).Returns(provider);
            var versionRepository = new VersionRepository(MongoClient);
            var handler = new ReadModelHandler<MatchReadModelHandler>(
                mockEvents.Object,
                versionRepository,
                new MatchReadModelHandler(mockMatchRepo),
                provider
                );

            var readModelHandlerNewInstance = new ReadModelHandler<MatchReadModelHandler>(
                mockEvents.Object,
                versionRepository,
                new MatchReadModelHandler(mockMatchRepo),
                serviceScopeFactoryMock.Object);
            serviceProviderMock.Setup(s => s.GetService<ReadModelHandler<MatchReadModelHandler>>()).Returns(
                readModelHandlerNewInstance);

            await handler.Update();

            await Task.Delay(5000);

            var mongoDatabase = MongoClient.GetDatabase("W3Champions-Statistic-Service");
            var realCollection = mongoDatabase.GetCollection<Matchup>(nameof(Matchup));
            var tempCollection = mongoDatabase.GetCollection<Matchup>(nameof(Matchup) + "_temp");

            var allRealMatches = await realCollection.Find(r => true).ToListAsync();
            var allTempMatches = await tempCollection.Find(r => true).ToListAsync();

            Assert.AreEqual(2, allRealMatches.Count);
            Assert.AreEqual(2, allTempMatches.Count);
        }
    }
}