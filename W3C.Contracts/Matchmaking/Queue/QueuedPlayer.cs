using System.Collections.Generic;

namespace W3C.Contracts.Matchmaking.Queue
{
    public class QueuedPlayer
    {
        public float mmr { get; set; }
        public float rd { get; set; }
        public QueueQuantiles quantiles { get; set; }
        public int queueTime { get; set; }
        public bool isFloConnected { get; set; }
        public List<PlayerQueueData> playerData { get; set; }
    }
}