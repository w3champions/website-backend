using System.Collections.Generic;

namespace W3ChampionsStatisticService.Admin.Portraits
{
    public class PortraitsDefinitionCommand
    {
        public PortraitsDefinitionCommand(List<int> _ids, List<string> _groups = null)
        {
            Ids = _ids;
            Groups = _groups ?? new List<string>();
        }
        public List<int> Ids { get; set; }
        public List<string> Groups { get; set; }
    }
}