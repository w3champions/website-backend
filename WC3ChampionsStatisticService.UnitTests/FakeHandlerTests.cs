using System.Collections.Generic;
using System.Threading.Tasks;
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
        [Ignore("not used in this case anymore")]
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
            Assert.AreEqual(Race.NE, events[1].match.players[0].race);
            Assert.AreEqual(false, events[2].match.players[0].won);
            Assert.AreEqual(Race.HU, events[2].match.players[0].race);
        }

        [Test]
        [Ignore("not used in this case anymore")]
        public async Task OneGamePresentLocally()
        {
            var fakeEventSyncHandler = CreateSUT();
            var playerProfile = PlayerProfile.Create("peter#123");
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
        [Ignore("not used in this case anymore")]
        public async Task OneGamePresentLocally_DifferentGateways()
        {
            var fakeEventSyncHandler = CreateSUT();
            var playerProfileUs = PlayerProfile.Create("peter#123");
            var playerProfileEu = PlayerProfile.Create("peter#123");
            playerProfileUs.RecordWin(Race.HU, GameMode.GM_1v1, true);
            playerProfileUs.RecordWin(Race.HU, GameMode.GM_1v1, false);
            playerProfileEu.RecordWin(Race.NE, GameMode.GM_1v1, false);
            await _playerRepository.UpsertPlayer(playerProfileUs);
            await _playerRepository.UpsertPlayer(playerProfileEu);
            var playerStatePad = CreateFakePadPlayer();
            // means that there is 1 hu win, 1 hu loss and 1 ne win not recorded
            // so 1 hu win should be on eu, 1 hu loss on us and ne win on eu
            playerStatePad.account = "peter#123";
            playerStatePad.data.stats.human = new WinsAndLossesPad { wins = 2, losses = 2};
            playerStatePad.data.stats.night_elf = new WinsAndLossesPad { wins = 1, losses = 1};
            playerStatePad.data.ladder["10"] = new PadLadder { solo = new WinsAndLossesPad {  losses = 2, wins = 1 } };
            playerStatePad.data.ladder.Add("20", new PadLadder { solo = new WinsAndLossesPad { losses = 1, wins = 2 } });
            _padServiceMock.Setup(p => p.GetPlayer("peter#123")).ReturnsAsync(playerStatePad);

            await fakeEventSyncHandler.Update();

            var events = await _matchEventRepository.Load();

            Assert.AreEqual(3, events.Count);

            Assert.AreEqual(true, events[0].match.players[0].won);
            Assert.AreEqual(20, events[0].match.gateway);
            Assert.AreEqual(Race.HU, events[0].match.players[0].race);

            Assert.AreEqual(true, events[1].match.players[0].won);
            Assert.AreEqual(20, events[1].match.gateway);
            Assert.AreEqual(Race.NE, events[1].match.players[0].race);

            Assert.AreEqual(false, events[2].match.players[0].won);
            Assert.AreEqual(10, events[2].match.gateway);
            Assert.AreEqual(Race.HU, events[2].match.players[0].race);

            // Do complete intergration test now
            var handler = new PlayerModelHandler(_playerRepository);
            foreach (var matchFinishedEvent in events)
            {
                await handler.Update(matchFinishedEvent);
            }

            var playerUs = await _playerRepository.Load("peter#123@10");
            var playerEu = await _playerRepository.Load("peter#123@20");

            Assert.AreEqual(2, playerEu.TotalWins);
            Assert.AreEqual(1, playerEu.TotalLosses);

            Assert.AreEqual(1, playerUs.TotalWins);
            Assert.AreEqual(2, playerUs.TotalLosses);
            Assert.AreEqual(2, playerUs.GetWinsPerRace(Race.HU) + playerEu.GetWinsPerRace(Race.HU));
            Assert.AreEqual(1, playerUs.GetWinsPerRace(Race.NE) + playerEu.GetWinsPerRace(Race.NE));
            Assert.AreEqual(2, playerUs.GetLossPerRace(Race.HU) + playerEu.GetLossPerRace(Race.HU));
            Assert.AreEqual(1, playerUs.GetLossPerRace(Race.NE) + playerEu.GetLossPerRace(Race.NE));
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

        [Test]
        public async Task SaveAndLoadTempLosses()
        {
            var wins = TempRemainingLosses.Create("Peter").RemainingWins;
            wins[0].Count = 2;
            wins[4].Count = 1;

            var tempLossesRepo = new TempLossesRepo(MongoClient);
            await tempLossesRepo.SaveLosses("Peter", wins);
            var result = await tempLossesRepo.LoadLosses("Peter");

            Assert.AreEqual(5, result.Count);
            Assert.AreEqual(2, result[0].Count);
            Assert.AreEqual(0, result[1].Count);
            Assert.AreEqual(0, result[2].Count);
            Assert.AreEqual(0, result[3].Count);
            Assert.AreEqual(1, result[4].Count);

            Assert.AreEqual(Race.HU, result[0].Race);
            Assert.AreEqual(Race.OC, result[1].Race);
            Assert.AreEqual(Race.NE, result[2].Race);
            Assert.AreEqual(Race.UD, result[3].Race);
            Assert.AreEqual(Race.RnD, result[4].Race);
        }

        [Test]
        public async Task SaveAndLoadTempWins()
        {
            var wins = TempRemainingLosses.Create("Peter").RemainingWins;
            wins[0].Count = 2;
            wins[4].Count = 1;

            var tempLossesRepo = new TempLossesRepo(MongoClient);
            await tempLossesRepo.SaveWins("Peter", wins);
            var result = await tempLossesRepo.LoadWins("Peter");

            Assert.AreEqual(5, result.Count);
            Assert.AreEqual(2, result[0].Count);
            Assert.AreEqual(0, result[1].Count);
            Assert.AreEqual(0, result[2].Count);
            Assert.AreEqual(0, result[3].Count);
            Assert.AreEqual(1, result[4].Count);

            Assert.AreEqual(Race.HU, result[0].Race);
            Assert.AreEqual(Race.OC, result[1].Race);
            Assert.AreEqual(Race.NE, result[2].Race);
            Assert.AreEqual(Race.UD, result[3].Race);
            Assert.AreEqual(Race.RnD, result[4].Race);
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