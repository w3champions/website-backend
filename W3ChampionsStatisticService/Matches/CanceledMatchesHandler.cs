using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.Services;
using W3C.Contracts.Matchmaking;
using Serilog;
using System.Transactions;
using System;

namespace W3ChampionsStatisticService.Matches;

public class CanceledMatchesHandler(
    IMatchEventRepository eventRepository,
    IMatchRepository matchRepository,
    ITransactionCoordinator transactionCoordinator,
    ITrackingService trackingService) : IAsyncUpdatable
{
    private readonly IMatchEventRepository _eventRepository = eventRepository;
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly ITransactionCoordinator _transactionCoordinator = transactionCoordinator;
    private readonly ITrackingService _trackingService = trackingService;

    public async Task Update()
    {
        var nextEvents = await _eventRepository.LoadCanceledMatches();

        while (nextEvents.Count != 0)
        {
            foreach (var nextEvent in nextEvents)
            {
                try
                {
                    await using (var transaction = AsyncTransactionScope.Create(_transactionCoordinator))
                    {
                        await transaction.Start();
                        if (nextEvent.match.gameMode != GameMode.CUSTOM)
                        {
                            var ongoingMatch = await _matchRepository.LoadDetailsByOngoingMatchId(nextEvent.match.id);

                            if (ongoingMatch == null)
                            {
                                Log.Warning($"Canceled match {nextEvent.match.id} not found");
                            }
                            else
                            {
                                if (ongoingMatch.Match != null)
                                {
                                    Log.Information($"Canceling ongoing match {ongoingMatch.Match.Id}");
                                    await _matchRepository.DeleteOnGoingMatch(ongoingMatch.Match);
                                }
                                else
                                {
                                    Log.Warning($"Canceled match detail had null Match property for {nextEvent.match.id}");
                                    await _matchRepository.DeleteOnGoingMatch(new Matchup { MatchId = nextEvent.match.id });
                                }
                            }
                        }

                        await _eventRepository.DeleteCanceledEvent(nextEvent.Id);
                        transaction.Complete();
                    }
                }
                catch (Exception e)
                {
                    _trackingService?.TrackException(e, $"CanceledMatchesHandler died on event {nextEvent.Id}");
                    throw;
                }
            }

            nextEvents = await _eventRepository.LoadCanceledMatches();
        }
    }
}
