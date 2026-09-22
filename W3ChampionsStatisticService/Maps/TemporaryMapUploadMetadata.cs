using System;
using W3C.Contracts.GameObjects;

namespace W3ChampionsStatisticService.Maps;

/// <summary>The JSON `metadata` multipart part of design spec Appendix A.3.</summary>
public class TemporaryMapUploadMetadata
{
    /// <summary>Client hint only — the server recomputes sha1 from the received bytes and compares.</summary>
    public string Sha1 { get; set; }

    public string OriginalFileName { get; set; }

    public long FileSize { get; set; }

    /// <summary>Omitted when the pre-check returned ready/expired (no capture was run).</summary>
    public TemporaryMapCapture Capture { get; set; }
}

/// <summary>The WC3 lobby capture result the launcher derived (design spec §4.4).</summary>
public class TemporaryMapCapture
{
    /// <summary>"free" or "mapped-forces".</summary>
    public string LobbyMode { get; set; }

    public int MaxTeams { get; set; }

    public int SlotCount { get; set; }

    public MapForce[] MappedForces { get; set; } = [];

    /// <summary>Logged by wb, not stored.</summary>
    public string LauncherVersion { get; set; }

    /// <summary>Logged by wb, not stored.</summary>
    public DateTime? CapturedAt { get; set; }
}
