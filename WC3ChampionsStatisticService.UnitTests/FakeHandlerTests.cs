using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.PadEvents.FakeEventSync;
using W3ChampionsStatisticService.PadEvents.PadSync;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class FakeHandlerTests : IntegrationTestBase
    {
        private Mock<IPadServiceRepo> _padServiceMock;
        private MatchEventRepository _matchEventRepository;
        private PlayerRepository _playerRepository;

        [Test]
        [Ignore("not needed right now")]
        public async Task NoEventPresentLocally()
        {
            var fakeEventSyncHandler = CreateSUT();
            var playerProfile = PlayerProfile.Create("peter#123");
            await _playerRepository.UpsertPlayer(playerProfile);
            _padServiceMock.Setup(p => p.GetPlayer("peter#123")).ReturnsAsync(CreateFakePadPlayer());

            await fakeEventSyncHandler.Update();

            var events = await _matchEventRepository.Load();

            Assert.AreEqual(3, events.Count);
            Assert.AreEqual(true, events[0].match.players[0].won);
            Assert.AreEqual(Race.HU, events[0].match.players[0].race);
            Assert.AreEqual(true, events[1].match.players[0].won);
            Assert.AreEqual(Race.HU, events[1].match.players[0].race);
            Assert.AreEqual(false, events[2].match.players[0].won);
            Assert.AreEqual(Race.HU, events[2].match.players[0].race);
        }

        [Test]
        public async Task OnlyOneRankIsSyncedBecausePreviousWasSynced()
        {
            var matchEventRepository = new MatchEventRepository(MongoClient);
            var rankRepository = new Mock<IRankRepository>();
            var rankHandler = new RankHandler(rankRepository.Object, matchEventRepository);

            await InsertRankChangedEvent(TestDtoHelper.CreateRankChangedEvent("peter#123"));

            await rankHandler.Update();

            rankRepository.Verify(r => r.InsertRanks(It.Is<List<Rank>>(rl => rl.Count == 1)));

            await InsertRankChangedEvent(TestDtoHelper.CreateRankChangedEvent("wolf#456"));

            await rankHandler.Update();

            rankRepository.Verify(r => r.InsertRanks(It.Is<List<Rank>>(rl => rl.Count == 1)));
        }

        [Test]
        public async Task EmptyRanksDoesNothing()
        {
            var matchEventRepository = new MatchEventRepository(MongoClient);
            var rankRepository = new Mock<IRankRepository>();
            var rankHandler = new RankHandler(rankRepository.Object, matchEventRepository);

            await InsertRankChangedEvent(TestDtoHelper.CreateRankChangedEvent("peter#123"));

            await rankHandler.Update();

            rankRepository.Verify(r => r.InsertRanks(It.Is<List<Rank>>(rl => rl.Count == 1)), Times.Once);

            await rankHandler.Update();

            rankRepository.Verify(r => r.InsertRanks(It.IsAny<List<Rank>>()), Times.Once);
        }

        [Test]
        [Ignore("not needed right now")]
        public async Task OneGamePresentLocally()
        {
            var fakeEventSyncHandler = CreateSUT();
            var playerProfile = PlayerProfile.Create("peter#123");
            playerProfile.RecordWin(Race.HU, GameMode.GM_1v1, GateWay.America, 0, true);
            await _playerRepository.UpsertPlayer(playerProfile);
            _padServiceMock.Setup(p => p.GetPlayer("peter#123")).ReturnsAsync(CreateFakePadPlayer());

            await fakeEventSyncHandler.Update();

            var events = await _matchEventRepository.Load();

            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(true, events[0].match.players[0].won);
            Assert.AreEqual(Race.HU, events[0].match.players[0].race);
            Assert.AreEqual(false, events[1].match.players[0].won);
            Assert.AreEqual(Race.HU, events[1].match.players[0].race);
        }

        [Test]
        [Ignore("not needed right now")]
        public async Task OneGamePresentLocally_DifferentGateways()
        {
            var fakeEventSyncHandler = CreateSUT();
            var playerProfile = PlayerProfile.Create("peter#123");
            playerProfile.RecordWin(Race.HU, GameMode.GM_1v1, GateWay.America, 0, true);
            playerProfile.RecordWin(Race.HU, GameMode.GM_1v1, GateWay.America, 0, false);
            playerProfile.RecordWin(Race.NE, GameMode.GM_1v1, GateWay.Europe, 0, false);
            await _playerRepository.UpsertPlayer(playerProfile);
            var playerStatePad = CreateFakePadPlayer();

            playerStatePad.account = "peter#123";
            playerStatePad.data.stats.human = new WinsAndLossesPad { wins = 2, losses = 2};
            playerStatePad.data.stats.night_elf = new WinsAndLossesPad { wins = 1, losses = 1};
            playerStatePad.data.ladder["10"] = new PadLadder { solo = new WinsAndLossesPad {  losses = 2, wins = 1 } };
            playerStatePad.data.ladder.Add("20", new PadLadder { solo = new WinsAndLossesPad { losses = 1, wins = 2 } });
            _padServiceMock.Setup(p => p.GetPlayer("peter#123")).ReturnsAsync(playerStatePad);

            await fakeEventSyncHandler.Update();

            var events = await _matchEventRepository.Load();

            Assert.AreEqual(3, events.Count);

            Assert.AreEqual(false, events[0].match.players[0].won);
            Assert.AreEqual(GateWay.America, events[0].match.gateway);
            Assert.AreEqual(Race.HU, events[0].match.players[0].race);

            Assert.AreEqual(true, events[1].match.players[0].won);
            Assert.AreEqual(GateWay.Europe, events[1].match.gateway);
            Assert.AreEqual(Race.HU, events[1].match.players[0].race);

            Assert.AreEqual(true, events[2].match.players[0].won);
            Assert.AreEqual(GateWay.Europe, events[2].match.gateway);
            Assert.AreEqual(Race.HU, events[2].match.players[0].race);

            // Do complete intergration test now
            var handler = new PlayerModelHandler(_playerRepository);
            foreach (var matchFinishedEvent in events)
            {
                await handler.Update(matchFinishedEvent);
            }

            var playerUs = await _playerRepository.LoadPlayer("peter#123");

            Assert.AreEqual(3, playerUs.TotalWins);
            Assert.AreEqual(3, playerUs.TotalLosses);
            Assert.AreEqual(3, playerUs.GetWinsPerRace(Race.HU));
            Assert.AreEqual(0, playerUs.GetWinsPerRace(Race.NE));
            Assert.AreEqual(2, playerUs.GetLossPerRace(Race.HU));
            Assert.AreEqual(1, playerUs.GetLossPerRace(Race.NE));
        }

        private FakeEventSyncHandler CreateSUT()
        {
            _padServiceMock = new Mock<IPadServiceRepo>();
            _matchEventRepository = new MatchEventRepository(MongoClient);
            _playerRepository = new PlayerRepository(MongoClient);
            return new FakeEventSyncHandler(
                _padServiceMock.Object,
                new VersionRepository(MongoClient),
                _matchEventRepository,
                _playerRepository,
                new FakeEventCreator());
        }

        private PlayerStatePad CreateFakePadPlayer()
        {
            var statePad = new PlayerStatePad
            {
                account = "peter#123",
                data = new Data
                {
                    ladder = new Dictionary<string, PadLadder>
                    {
                        { "10", new PadLadder { solo = new WinsAndLossesPad { losses = 1, wins = 2 } } }
                    },
                    stats = new Stats
                    {
                        human = new WinsAndLossesPad { wins = 1, losses = 1 },
                        night_elf = new WinsAndLossesPad { wins = 1 },
                        orc = new WinsAndLossesPad(),
                        undead = new WinsAndLossesPad(),
                        random = new WinsAndLossesPad(),
                    },
                }
            };

            return statePad;
        }

    }
}