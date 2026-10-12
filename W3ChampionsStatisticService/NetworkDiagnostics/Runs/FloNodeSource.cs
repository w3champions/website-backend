using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Runs;

public interface IFloNodeSource
{
    /// <summary>Throws HttpRequestException when matchmaking is unreachable or fails.</summary>
    Task<List<FloNodeInfo>> GetFloNodesAsync();
}

public class MatchmakingFloNodeSource(MatchmakingServiceClient client) : IFloNodeSource
{
    public Task<List<FloNodeInfo>> GetFloNodesAsync() => client.GetFloNodes();
}
