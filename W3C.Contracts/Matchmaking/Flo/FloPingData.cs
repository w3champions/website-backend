using System.Collections.Generic;

namespace W3C.Contracts.Matchmaking.Flo;

public class FloPingData
{
    public List<FloServerPingData> floPings { get; set; }
    public FloClosestServerData closestNode { get; set; }
}
