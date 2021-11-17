using System.Collections.Generic;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class PortraitsCommand
    {
        public PortraitsCommand()
        {
            BnetTags = new List<string>();
            Portraits = new List<int>();
            Tooltip = "";
        }

        public List<string> BnetTags { get; set; }
        public List<int> Portraits { get; set; }
        public string Tooltip { get; set; }
    }
}