using System.Collections.Generic;

namespace W3C.Contracts.Matchmaking.Queue;

public class Queue 
{
    public int gameMode { get; set; }
    public List<QueuedPlayer> snapshot { get; set; }
}
