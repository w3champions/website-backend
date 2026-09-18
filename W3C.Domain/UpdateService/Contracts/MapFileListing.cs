using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace W3C.Domain.UpdateService.Contracts;

/// <summary>
/// GET /api/content/maps/files (x-admin-secret) — design spec Appendix A.7. <c>files</c> is required: a page
/// without it is a contract violation, never empty.
/// </summary>
public class MapFileListingResponse
{
    [JsonProperty(Required = Required.Always)]
    public List<MapFileListingEntry> Files { get; set; } = [];

    /// <summary>Opaque cursor for the next page; null or empty when the listing is exhausted.</summary>
    public string Next { get; set; }
}

public class MapFileListingEntry
{
    public string FilePath { get; set; }
    public long SizeBytes { get; set; }
    public DateTime ModifiedAt { get; set; }
}
