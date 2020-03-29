using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class ReadModelHandlerBaseTests : IntegrationTestBase
    {
        [Test]
        public async Task InsertMatches()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();

            fakeEvent.data.mapInfo.name = "Twisted Meadows";
            var mockEvents = new Mock<IMatchEventRepository>();
            mockEvents.SetupSequence(m => m.Load(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new List<MatchFinishedEvent>() { fakeEvent })
                .ReturnsAsync(new List<MatchFinishedEvent>());

            var mockMatchRepo = new Mock<IMatchRepository>();

            var versionRepository = new Mock<IVersionRepository>();

            var handler = new PopulateReadModelHandler<PopulateMatchReadModelHandler>(
                mockEvents.Object,
                versionRepository.Object,
                new PopulateMatchReadModelHandler(mockMatchRepo.Object));

            await handler.Update();

            mockMatchRepo.Verify(m => m.Insert(It.Is<List<Matchup>>(ma => ma.Single().Map == "Twisted Meadows")), Times.Once);
        }
    }
}