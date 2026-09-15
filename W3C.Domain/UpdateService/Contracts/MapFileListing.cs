using System;
using System.Collections.Generic;

namespace W3C.Domain.UpdateService.Contracts;

/// <summary>GET /api/content/maps/files (x-admin-secret) — design spec Appendix A.7.</summary>
public class MapFileListingResponse
{
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
