using W3C.Contracts.Matchmaking.Flo;

namespace W3C.Contracts.Matchmaking.Queue;

public class PlayerQueueData
{
    public string battleTag { get; set; }
    public FloPingData floInfo { get; set; }
    public string location { get; set; }
    public string serverOption { get; set; }
}
