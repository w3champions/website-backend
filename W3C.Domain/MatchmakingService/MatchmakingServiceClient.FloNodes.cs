using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace W3C.Domain.MatchmakingService;

/// <summary>A flo node as served by matchmaking's public GET /flo/nodes. Optional fields may be absent or null.</summary>
public class FloNodeInfo
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Location { get; set; }
    public string CountryId { get; set; }

    /// <summary>The node's IP address (MM splits flo's "ip:port" into ipAddress + port). Null when MM omits it.</summary>
    public string IpAddress { get; set; }

    public int? Port { get; set; }
    public FloNodeInfoSettings Settings { get; set; } = new();
}

public class FloNodeInfoSettings
{
    public bool IsDisabled { get; set; }
}

public partial class MatchmakingServiceClient
{
    /// <summary>All flo nodes with location and enabled state (MM public GET /flo/nodes).</summary>
    public async Task<List<FloNodeInfo>> GetFloNodes()
    {
        var response = await _httpClient.GetAsync($"{MatchmakingApiUrl}/flo/nodes");
        if (response.IsSuccessStatusCode)
        {
            var nodes = await GetResult<List<FloNodeInfo>>(response) ?? [];
            return nodes
                .Where(n => n != null)
                .Select(n =>
                {
                    n.Settings ??= new FloNodeInfoSettings();
                    return n;
                })
                .ToList();
        }

        await HandleMMError(response);
        return [];
    }
}
