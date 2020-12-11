using System.Collections.Generic;

namespace W3ChampionsStatisticService.Matches
{
    public class ServerInfo
    {
        public ServerInfo()
        {
            PlayerServerInfos = new List<PlayerServerInfo>();
        }

        public string Provider { get; set; }
        public int? NodeId { get; set; }
        public string CountryCode { get; set; }
        public string Location { get; set; }
        public string Name { get; set; }
        public List<PlayerServerInfo> PlayerServerInfos { get; set; }
    }
}
