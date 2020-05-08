using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using W3ChampionsStatisticService.PadEvents.PadSync;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PadEvents.FakeEventSync
{
    public class FakeEventSyncHandler : IAsyncUpdatable
    {
        private readonly IPadServiceRepo _padRepo;
        private readonly IVersionRepository _versionRepository;
        private readonly IMatchEventRepository _matchEventRepository;
        private readonly IPlayerRepository _playerRepository;
        private readonly FakeEventCreator _fakeEventCreator;
        private readonly ILogger<FakeEventSyncHandler> _logger;

        public FakeEventSyncHandler(
            IPadServiceRepo padRepo,
            IVersionRepository versionRepository,
            IMatchEventRepository matchEventRepository,
            IPlayerRepository playerRepository,
            FakeEventCreator fakeEventCreator,
            ILogger<FakeEventSyncHandler> logger = null
            )
        {
            _padRepo = padRepo;
            _versionRepository = versionRepository;
            _matchEventRepository = matchEventRepository;
            _playerRepository = playerRepository;
            _fakeEventCreator = fakeEventCreator;
            _logger = logger ?? new NullLogger<FakeEventSyncHandler>();
            ;
        }

        public async Task Update()
        {
            var lastVersion = await _versionRepository.GetLastVersion<FakeEventSyncHandler>();
            var offset = int.Parse(lastVersion);

            _logger.LogWarning("starting");
            var ids = (await _playerRepository.LoadAllIds()).Skip(offset);

            foreach (var id in ids)
            {
                var playerOnMySide = await _playerRepository.Load(id);
                if (playerOnMySide == null) continue;

                var player = await _padRepo.GetPlayer(id);
                if (player == null) continue;

                var fakeEvents = _fakeEventCreator.CreatFakeEvents(player, playerOnMySide, offset);

                if (fakeEvents.Any())
                {
                    _logger.LogWarning($"Events for {playerOnMySide.BattleTag} with {fakeEvents.Count}");
                }

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
}