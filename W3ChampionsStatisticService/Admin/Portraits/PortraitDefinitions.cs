using System.Collections.Generic;

namespace W3ChampionsStatisticService.Admin.Portraits
{
    public class PortraitDefinitions
    {
        public PortraitDefinitions(List<int> _ids)
        {
            Ids = _ids;
        }

        public List<int> Ids { get; set; }
    }
}
