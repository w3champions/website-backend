using System.Collections.Generic;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Admin.Portraits
{
    public class PortraitDefinition : IIdentifiable
    {
        public PortraitDefinition(int _id, List<string> _group = null)
        {
            Number = _id;
            Groups = _group;
        }

        public string Id => Number.ToString();
        public int Number { get; set; }
        public List<string> Groups { get; set; }
    }
}
