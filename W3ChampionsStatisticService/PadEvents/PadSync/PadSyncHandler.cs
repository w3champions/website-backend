using System;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PadEvents.PadSync
{
    public class PadSyncHandler : IAsyncUpdatable
    {
        private readonly PadServiceRepo _padRepo;
        private readonly IVersionRepository _versionRepository;
        private readonly IMatchEventRepository _matchEventRepository;

        public PadSyncHandler(
            PadServiceRepo padRepo,
            IVersionRepository versionRepository,
            IMatchEventRepository matchEventRepository
            )
        {
            _padRepo = padRepo;
            _versionRepository = versionRepository;
            _matchEventRepository = matchEventRepository;
        }

        public async Task Update()
        {
            var handlerVersion = await _versionRepository.GetLastVersion<PadSyncHandler>();
            var lastVersion = handlerVersion.Version;
            if (lastVersion == null) lastVersion = "0";

            var offset = long.Parse(lastVersion);
            var events = await _padRepo.GetFrom(offset);
            if (!events.Any())
            {
                await _versionRepository.SaveLastVersion<PadSyncHandler>((offset + 100).ToString());
                Console.WriteLine("No events found anymore");
            }

            int count = 0;

            while (events.Any())
            {
                foreach (var finishedEvent in events)
                {
                    if (finishedEvent.state == 2)
                    {
                        var wasInserted = await _matchEventRepository.InsertIfNotExisting(
                            new MatchFinishedEvent { match = finishedEvent},
                            count);

                        if (wasInserted)
                        {
                            count++;
                        }
                    }
                }

                offset += 100;
                await _versionRepository.SaveLastVersion<PadSyncHandler>(offset.ToString());
                events = await _padRepo.GetFrom(offset);
                await Task.Delay(1000);
            }
        }
    }
}