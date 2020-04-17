using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
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

            fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";
            fakeEvent.match.state = 2;
            var mockEvents = new Mock<IMatchEventRepository>();
            mockEvents.SetupSequence(m => m.Load(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new List<MatchFinishedEvent>() { fakeEvent })
                .ReturnsAsync(new List<MatchFinishedEvent>());

            var mockMatchRepo = new Mock<IMatchRepository>();

            var versionRepository = new Mock<IVersionRepository>();

            var handler = new ReadModelHandler<MatchReadModelHandler>(
                mockEvents.Object,
                versionRepository.Object,
                new MatchReadModelHandler(mockMatchRepo.Object));

            await handler.Update();

            mockMatchRepo.Verify(m => m.Insert(It.Is<Matchup>(ma => ma.Map == "amazonia")), Times.Once);
        }

        [Test]
        public async Task InsertMatchesFail1()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();

            fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";
            fakeEvent.match.state = 3;
            var mockEvents = new Mock<IMatchEventRepository>();
            mockEvents.SetupSequence(m => m.Load(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new List<MatchFinishedEvent>() { fakeEvent })
                .ReturnsAsync(new List<MatchFinishedEvent>());

            var mockMatchRepo = new Mock<IMatchRepository>();

            var versionRepository = new Mock<IVersionRepository>();

            var handler = new ReadModelHandler<MatchReadModelHandler>(
                mockEvents.Object,
                versionRepository.Object,
                new MatchReadModelHandler(mockMatchRepo.Object));

            await handler.Update();

            mockMatchRepo.Verify(m => m.Insert(It.IsAny<Matchup>()), Times.Never);
        }

    }
}