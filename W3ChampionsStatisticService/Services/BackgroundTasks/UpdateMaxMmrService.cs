using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.Common.Constants;
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
                int maxMmr = _playerRepository.LoadMaxMMR();
                MmrConstants.CurrentMaxMmr = maxMmr;
                _logger.LogInformation($"MaxMmr updated to {maxMmr} at {DateTime.UtcNow}");
            }
            catch (Exception ex)
            {
              _logger.LogError(ex, "Error updating MaxMmr");
            }
            await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
        }
    }
}
