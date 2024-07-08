using System.Collections.Generic;

namespace W3ChampionsStatisticService.Admin;

public class ProxyUpdate
{
    public ProxyUpdate()
    {
        nodeOverrides = new List<string>();
        automaticNodeOverrides = new List<string>();
    }
    public List<string> nodeOverrides { get; set; }
    public List<string> automaticNodeOverrides { get; set; }
}
