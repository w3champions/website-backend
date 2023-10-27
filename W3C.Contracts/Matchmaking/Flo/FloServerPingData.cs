namespace W3C.Contracts.Matchmaking.Flo;

public class FloServerPingData
{
    public int nodeId { get; set; }
    public int currentPing { get; set; }
    public int avgPing { get; set; }
    public int lossRate { get; set; }
    public int pingFilter { get; set; }
}
