using System.Collections.Generic;

namespace W3ChampionsStatisticService.Admin
{
    public class ProxyUpdate
    {
        public ProxyUpdate()
        {
            this.nodeOverrides = new List<string>();
            this.automaticNodeOverrides = new List<string>();
        }
        public List<string> nodeOverrides { get; set; }
        public List<string> automaticNodeOverrides { get; set; }
    }
}