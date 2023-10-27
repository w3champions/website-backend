using System;
using W3C.Domain.Repositories;

namespace W3C.Domain.CommonValueObjects;

public class Patch : IIdentifiable
{
    public string Version { get; set; }
    public DateTime StartDate { get; set; }
    public string Id => PatchId;
    public string PatchId { get; set; }
}
