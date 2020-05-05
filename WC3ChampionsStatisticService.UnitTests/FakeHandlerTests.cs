using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.PadEvents.FakeEventSync;
using W3ChampionsStatisticService.PadEvents.PadSync;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.ReadModelBase;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class FakeHandlerTests : IntegrationTestBase
    {
        private Mock<IPadServiceRepo> _padServiceMock;
        private MatchEventRepository _matchEventRepository;
        private PlayerRepository _playerRepository;
        private TempLossesRepo _tempLossesRepo;

        [Test]
        public async Task NoEventPresentLocally()
        {
            var fakeEventSyncHandler = CreateSUT();
            var playerProfile = PlayerProfile.Create("peter#123@10", "peter#123");
            await _playerRepository.UpsertPlayer(playerProfile);
            _padServiceMock.Setup(p => p.GetPlayer("peter#123")).ReturnsAsync(CreateFakePadPlayer());

            await fakeEventSyncHandler.Update();

            var events = await _matchEventRepository.Load();

            Assert.AreEqual(3, events.Count);
            Assert.AreEqual(true, events[0].match.players[0].won);
            Assert.AreEqual(Race.HU, events[0].match.players[0].race);
            Assert.AreEqual(true, events[1].match.players[0].won);
            Assert.AreEqual(Race.NE, events[1].match.players[0].race);
            Assert.AreEqual(false, events[2].match.players[0].won);
            Assert.AreEqual(Race.HU, events[2].match.players[0].race);
        }

        [Test]
        public async Task OneGamePresentLocally()
        {
            var fakeEventSyncHandler = CreateSUT();
            var playerProfile = PlayerProfile.Create("peter#123@10", "peter#123");
            playerProfile.RecordWin(Race.HU, GameMode.GM_1v1, true);
            await _playerRepository.UpsertPlayer(playerProfile);
            _padServiceMock.Setup(p => p.GetPlayer("peter#123")).ReturnsAsync(CreateFakePadPlayer());

            await fakeEventSyncHandler.Update();

            var events = await _matchEventRepository.Load();

            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(true, events[0].match.players[0].won);
            Assert.AreEqual(Race.NE, events[0].match.players[0].race);
            Assert.AreEqual(false, events[1].match.players[0].won);
            Assert.AreEqual(Race.HU, events[1].match.players[0].race);
        }

        [Test]
        public async Task OneGamePresentLocally_DifferentGateways()
        {
            var fakeEventSyncHandler = CreateSUT();
            var playerProfileUs = PlayerProfile.Create("peter#123@10", "peter#123");
            var playerProfileEu = PlayerProfile.Create("peter#123@20", "peter#123");
            playerProfileUs.RecordWin(Race.HU, GameMode.GM_1v1, true);
            playerProfileUs.RecordWin(Race.HU, GameMode.GM_1v1, false);
            playerProfileEu.RecordWin(Race.NE, GameMode.GM_1v1, false);
            await _playerRepository.UpsertPlayer(playerProfileUs);
            await _playerRepository.UpsertPlayer(playerProfileEu);
            var playerStatePad = CreateFakePadPlayer();
            // means that there is 1 hu win, 1 hu loss and 1 ne win not recorded
            // so 1 hu win should be on eu, 1 hu loss on us and ne win on eu
            playerStatePad.Data.Stats.Human = new WinAndLossesPad { Wins = 2, Losses = 2};
            playerStatePad.Data.Stats.NightElf = new WinAndLossesPad { Wins = 1, Losses = 1};
            playerStatePad.Data.Ladder["10"] = new PadLadder { Losses = 2, Wins = 1 };
            playerStatePad.Data.Ladder.Add("20", new PadLadder { Losses = 1, Wins = 2 });
            _padServiceMock.Setup(p => p.GetPlayer("peter#123")).ReturnsAsync(playerStatePad);

            await fakeEventSyncHandler.Update();

            var events = await _matchEventRepository.Load();

            Assert.AreEqual(3, events.Count);
            Assert.AreEqual(false, events[0].match.players[0].won);
            Assert.AreEqual(Race.HU, events[0].match.players[0].race);
            Assert.AreEqual(true, events[1].match.players[0].won);
            Assert.AreEqual(Race.NE, events[1].match.players[0].race);
            Assert.AreEqual(false, events[2].match.players[0].won);
            Assert.AreEqual(Race.NE, events[2].match.players[0].race);
        }

        private FakeEventSyncHandler CreateSUT()
        {
            _padServiceMock = new Mock<IPadServiceRepo>();
            _matchEventRepository = new MatchEventRepository(MongoClient);
            _playerRepository = new PlayerRepository(MongoClient);
            _tempLossesRepo = new TempLossesRepo(MongoClient);
            return new FakeEventSyncHandler(
                _padServiceMock.Object,
                new VersionRepository(MongoClient),
                _matchEventRepository,
                _playerRepository,
                new FakeEventCreator(_tempLossesRepo));
        }


        private PlayerStatePad CreateFakePadPlayer()
        {
            var statePad = new PlayerStatePad();
            statePad.Data = new Data
            {
                Ladder = new Dictionary<string, PadLadder>
                {
                    {"10", new PadLadder {Losses = 1, Wins = 2}}
                },
                Stats = new Stats
                {
                    Human = new WinAndLossesPad {Wins = 1, Losses = 1},
                    NightElf = new WinAndLossesPad {Wins = 1},
                    Orc = new WinAndLossesPad(),
                    Undead = new WinAndLossesPad(),
                    Random = new WinAndLossesPad(),
                },
            };

            return statePad;
        }

    }
}