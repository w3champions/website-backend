using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PadEvents.PadSync;
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
        private readonly DateTime _dateTime = DateTime.Now.AddDays(-60);

        public FakeEventSyncHandler(
            PadServiceRepo padRepo,
            IVersionRepository versionRepository,
            IMatchEventRepository matchEventRepository,
            IPlayerRepository playerRepository
            )
        {
            _padRepo = padRepo;
            _versionRepository = versionRepository;
            _matchEventRepository = matchEventRepository;
            _playerRepository = playerRepository;
        }

        public async Task Update()
        {
            var lastVersion = await _versionRepository.GetLastVersion<FakeEventSyncHandler>();
            if (lastVersion == null) lastVersion = "0";

            var offset = int.Parse(lastVersion);

            while (true)
            {
                var playerOnMySide = await _playerRepository.LoadOverviewFrom(offset);
                if (playerOnMySide == null) break;
                if (playerOnMySide.PlayerIds.Count != 1)
                {
                    offset += 1;
                    await _versionRepository.SaveLastVersion<FakeEventSyncHandler>(offset.ToString());
                    continue;
                }

                var id = playerOnMySide.PlayerIds.Single();
                var player = await _padRepo.GetPlayerFrom($"{id.Name}#{id.BattleTag}");

                var fakeEvents = CreatFakeEvents(player, playerOnMySide, offset);
                foreach (var finishedEvent in fakeEvents)
                {
                    await _matchEventRepository.InsertIfNotExisting(finishedEvent);
                }

                offset += 1;
                await _versionRepository.SaveLastVersion<FakeEventSyncHandler>(offset.ToString());
                await Task.Delay(1000);
            }
        }

        private IEnumerable<MatchFinishedEvent> CreatFakeEvents(PlayerStatePad player, PlayerOverview myPlayer, int increment)
        {
            return new List<MatchFinishedEvent>
            {
                new MatchFinishedEvent { match = null, WasFakeEvent = true, Id = new ObjectId(_dateTime, 0,0, increment)}
            };
        }
    }
}