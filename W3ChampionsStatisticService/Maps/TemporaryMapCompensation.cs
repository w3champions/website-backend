using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using W3C.Domain.UpdateService;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// §6.3 step 9: rolls stored bytes back with an update-service delete. Deliberately NOT cancellable: the client may
/// already have gone, which is exactly when an orphan would otherwise be left. Retries after 1, 2 and 4 s, then logs
/// ORPHAN; the sweep's reconciliation pass reclaims an unclaimed file after 24 h.
/// <para>
/// Retrying a delete later is safe only because the caller holds the fileKey's <see cref="TemporaryMapFileKeyLock"/>
/// throughout: no other upload can have stored bytes at the fileKey in the meantime (S-L1).
/// </para>
/// </summary>
internal sealed class TemporaryMapCompensation(UpdateServiceClient updateServiceClient, ILogger logger)
{
    private static readonly TimeSpan[] Delays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    private readonly UpdateServiceClient _updateServiceClient = updateServiceClient;
    private readonly ILogger _logger = logger;

    /// <summary>Test seam: how the retries wait. Never cancellable.</summary>
    internal Func<TimeSpan, Task> WaitAsync { get; init; } = delay => Task.Delay(delay, CancellationToken.None);

    public async Task DeleteAsync(string fileKey)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _updateServiceClient.DeleteMapFileByPathAsync(fileKey, CancellationToken.None);
                _logger.LogInformation("Compensated temporary map file {FileKey}", fileKey);
                return;
            }
            catch (Exception ex) when (attempt < Delays.Length)
            {
                _logger.LogDebug(ex, "Compensation attempt {Attempt} for {FileKey} failed; retrying", attempt + 1, fileKey);
                await WaitAsync(Delays[attempt]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ORPHAN temporary map file left in update-service at {FileKey}", fileKey);
                return;
            }
        }
    }
}
