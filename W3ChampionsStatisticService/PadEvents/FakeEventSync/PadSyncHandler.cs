using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents.PadSync;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PadEvents.FakeEventSync
{
    public class FakeEventSyncHandler : IAsyncUpdatable
    {
        private readonly PadServiceRepo _padRepo;
        private readonly IVersionRepository _versionRepository;
        private readonly IMatchEventRepository _matchEventRepository;
        private readonly IPlayerRepository _playerRepository;
        private readonly FakeEventCreator _fakeEventCreator;

        public FakeEventSyncHandler(
            PadServiceRepo padRepo,
            IVersionRepository versionRepository,
            IMatchEventRepository matchEventRepository,
            IPlayerRepository playerRepository,
            FakeEventCreator fakeEventCreator
            )
        {
            _padRepo = padRepo;
            _versionRepository = versionRepository;
            _matchEventRepository = matchEventRepository;
            _playerRepository = playerRepository;
            _fakeEventCreator = fakeEventCreator;
        }

        public async Task Update()
        {
            var lastVersion = await _versionRepository.GetLastVersion<FakeEventSyncHandler>();
            if (lastVersion == null) lastVersion = "0";

            var offset = int.Parse(lastVersion);

            while (true)
            {
                var playerOnMySide = await _playerRepository.LoadPlayerFrom(offset);
                if (playerOnMySide == null) break;

                var player = await _padRepo.GetPlayer($"{playerOnMySide.Name}#{playerOnMySide.BattleTag}");

                var fakeEvents = _fakeEventCreator.CreatFakeEvents(player, playerOnMySide, offset);
                foreach (var finishedEvent in fakeEvents)
                {
                    await _matchEventRepository.InsertIfNotExisting(finishedEvent);
                }

                offset += 1;
                await _versionRepository.SaveLastVersion<FakeEventSyncHandler>(offset.ToString());
                await Task.Delay(1000);
            }
        }
    }

    public class FakeEventCreator
    {
        private DateTime _dateTime = DateTime.Now.AddDays(-60);

        public IEnumerable<MatchFinishedEvent> CreatFakeEvents(PlayerStatePad player, PlayerProfile myPlayer, int increment)
        {
            _dateTime = _dateTime.AddSeconds(increment);
            var gateWay = myPlayer.Id.Split("@")[1];
            player.Data.Ladder.TryGetValue(gateWay, out var gatewayStats);

            var matchFinishedEvents = new List<MatchFinishedEvent>();
            var winDiffs = new List<RaceAndWinDto>();
            var lossDiffs = new List<RaceAndWinDto>();

            winDiffs.Add(new RaceAndWinDto(Race.HU, player.Data.Stats.Human.Wins - myPlayer.GetWinsPerRace(Race.HU)));
            lossDiffs.Add(new RaceAndWinDto(Race.HU, player.Data.Stats.Human.Wins - myPlayer.GetLossPerRace(Race.HU)));

            winDiffs.Add(new RaceAndWinDto(Race.OC, player.Data.Stats.Human.Wins - myPlayer.GetWinsPerRace(Race.OC)));
            lossDiffs.Add(new RaceAndWinDto(Race.OC, player.Data.Stats.Human.Wins - myPlayer.GetLossPerRace(Race.OC)));

            winDiffs.Add(new RaceAndWinDto(Race.NE, player.Data.Stats.Human.Wins - myPlayer.GetWinsPerRace(Race.NE)));
            lossDiffs.Add(new RaceAndWinDto(Race.NE, player.Data.Stats.Human.Wins - myPlayer.GetLossPerRace(Race.NE)));

            winDiffs.Add(new RaceAndWinDto(Race.UD, player.Data.Stats.Human.Wins - myPlayer.GetWinsPerRace(Race.UD)));
            lossDiffs.Add(new RaceAndWinDto(Race.UD, player.Data.Stats.Human.Wins - myPlayer.GetLossPerRace(Race.UD)));

            winDiffs.Add(new RaceAndWinDto(Race.RnD, player.Data.Stats.Human.Wins - myPlayer.GetWinsPerRace(Race.RnD)));
            lossDiffs.Add(new RaceAndWinDto(Race.RnD, player.Data.Stats.Human.Wins - myPlayer.GetLossPerRace(Race.RnD)));

            matchFinishedEvents.AddRange(CreateGamesOfDiffs(true, myPlayer, increment, winDiffs));
            matchFinishedEvents.AddRange(CreateGamesOfDiffs(false, myPlayer, increment + matchFinishedEvents.Count, lossDiffs));

            return matchFinishedEvents;
        }

        private List<MatchFinishedEvent> CreateGamesOfDiffs(
            bool wins,
            PlayerProfile myPlayer,
            int increment,
            List<RaceAndWinDto> winDiffs)
        {
            var gateWay = myPlayer.Id.Split("@")[0];
            var finishedEvents = new List<MatchFinishedEvent>();
            foreach (var winDiff in winDiffs)
            {
                while (winDiff.Count != 0)
                {
                    finishedEvents.Add(new MatchFinishedEvent
                    {
                        match = CreatMatch(
                            wins,
                            int.Parse(gateWay),
                            myPlayer.CombinedBattleTag,
                            myPlayer.Id,
                            winDiff.Race),
                        WasFakeEvent = true,
                        Id = new
                            ObjectId
                            (_dateTime,
                                0, 0, increment)
                    });
                    increment++;
                    winDiff.Count--;
                }
            }

            return finishedEvents;
        }

        private Match CreatMatch(bool won, int gateWay, string battleTag, string playerId, Race race)
        {
            return new Match
            {
                id = Guid.NewGuid().ToString(),
                gameMode = GameMode.GM_1v1,
                gateway = gateWay,
                players = new List<PlayerMMrChange>
                {
                    new PlayerMMrChange
                    {
                        battleTag = battleTag,
                        won = won,
                        race = race,
                        id = playerId
                    },
                    new PlayerMMrChange
                    {
                        battleTag = "FakeEnemy#123",
                        won = !won,
                        race = Race.RnD,
                        id = "FakeEnemy#123@10"
                    }

                }

            };
        }
    }

    public class RaceAndWinDto
    {
        public RaceAndWinDto(Race race, long count)
        {
            Race = race;
            Count = count;
        }

        public Race Race { get; }
        public long Count { get; set; }
    }
}