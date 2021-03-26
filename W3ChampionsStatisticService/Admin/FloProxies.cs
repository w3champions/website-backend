using System.Collections.Generic;

namespace W3ChampionsStatisticService.Admin
{
    public class FloProxies
        {
            public FloProxies()
            {
                this.nodeOverrides = new List<string>();
                this.automaticNodeOverrides = new List<string>();
                this._id = "";
                this._created_at = "";
                this._updated_at = "";
            }
            public List<string> nodeOverrides { get; set; }
            public List<string> automaticNodeOverrides { get; set; }
            public string _id { get; set; }
            public string _created_at { get; set; }
            public string _updated_at { get; set; }
        }
}