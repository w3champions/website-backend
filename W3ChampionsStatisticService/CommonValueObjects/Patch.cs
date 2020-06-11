using System;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.CommonValueObjects
{
    public class Patch : IIdentifiable
    {
        public string Version { get; set; }

        public DateTime StartDate { get; set; }

        public string Id => PatchId;

        public string PatchId {get;set;}
    }
}