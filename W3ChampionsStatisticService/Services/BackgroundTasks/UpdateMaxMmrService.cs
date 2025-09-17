using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.Common.Constants;
using System.Linq;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.Services.BackgroundTasks;
public class UpdateMaxMmrService(ILogger<UpdateMaxMmrService> logger, PlayerRepository playerRepository) : BackgroundService
{
    private readonly ILogger<UpdateMaxMmrService> _logger = logger;
    private readonly PlayerRepository _playerRepository = playerRepository;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var gameMode in System.Enum.GetValues(typeof(W3C.Contracts.Matchmaking.GameMode)).Cast<W3C.Contracts.Matchmaking.GameMode>())
                {
                    int maxMmr = _playerRepository.LoadMaxMMR(gameMode);
                    MmrConstants.MaxMmrPerGameMode[gameMode] = maxMmr;
                    _logger.LogInformation($"MaxMmr for {gameMode} updated to {maxMmr} at {DateTime.UtcNow}");
                }
                // Optionally, update CurrentMaxMmr to the highest value across all game modes
                MmrConstants.CurrentMaxMmr = MmrConstants.MaxMmrPerGameMode.Values.Max();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating MaxMmr");
            }
            await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
        }
    }
}
