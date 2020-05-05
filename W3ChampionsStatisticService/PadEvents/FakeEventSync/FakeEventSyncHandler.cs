using System.Threading.Tasks;
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

        public FakeEventSyncHandler(
            IPadServiceRepo padRepo,
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

                var fakeEvents = await _fakeEventCreator.CreatFakeEvents(player, playerOnMySide, offset);

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