using System.Collections.Generic;
using Newtonsoft.Json;

namespace W3ChampionsStatisticService.Admin
{
    public class Queue 
    {
        public int gameMode { get; set; }
        public List<QueuedPlayer> snapshot { get; set; }
    }

    public class QueuedPlayer
    {
        public float mmr { get; set; }
        public float rd { get; set; }
        public QueueQuantiles quantiles { get; set; } 
        public int queueTime { get; set; }
        public bool isFloConnected { get; set; }
        public List<PlayerQueueData> playerData { get; set; }
    }

    public class QueueQuantiles
    {
        public float quantile { get; set; }
        public float activityQuantile { get; set; }
    }

    public class PlayerQueueData
    {
        public string battleTag { get; set; }
        public FloPingData floInfo { get; set; }
        public string location { get; set; }
        public string serverOption { get; set; }
    }
    public class FloPingData
    {
        public List<FloServerPingData> floPings { get; set; }
        public FloClosestServerData closestNode { get; set; }
    }

    public class FloServerPingData
    {
        public int nodeId { get; set; }
        public int currentPing { get; set; }
        public int avgPing { get; set; }
        public int lossRate { get; set; }
        public int pingFilter { get; set; }
    }

    public class FloClosestServerData
    {
        public string country_id { get; set; }
        public int id { get; set; }
        public string location { get; set; }
        public string name { get; set; }
        public bool isDisabled { get; set; }
        public bool isCnOptimized { get; set; }
    }
}