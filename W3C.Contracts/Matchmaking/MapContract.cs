using W3C.Contracts.GameObjects;

namespace W3C.Contracts.Matchmaking;

public class MapContract
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }
    public int MaxTeams { get; set; }
    public MapForce[] MappedForces { get; set; } = new MapForce[0];
    public GameMap GameMap { get; set; }
    public int teamSize { get; set; }
    public bool Disabled { get; set; }

    /// <summary>
    /// The map's stored file path (matchmaking's shortPath). For a temporary map this is the
    /// canonical fileKey, e.g. "W3Champions/CustomGames/Legion TD-94ec3bda.w3x".
    /// </summary>
    public string Path { get; set; }

    /// <summary>True for a self-provided (temporary) map. Absent/false means a permanent pool map.</summary>
    public bool Temporary { get; set; }

    /// <summary>battleTag of the last uploader. Set for temporary and permanent maps.</summary>
    public string Uploader { get; set; }

    /// <summary>"present" or "deleted" — temporary maps only. See TemporaryMapFileStates.</summary>
    public string FileState { get; set; }

    /// <summary>Epoch milliseconds of the last game START — temporary maps only.</summary>
    public long? LastHostedAt { get; set; }

    /// <summary>Slot count from the WC3 capture — temporary maps only.</summary>
    public int? SlotCount { get; set; }

    /// <summary>The uploader's original file name — temporary maps only.</summary>
    public string OriginalFileName { get; set; }

    // NOTE: the matchmaking records behind this contract can carry a credential-bearing field (the map
    // proof). website-backend must never deserialise or re-serve it, so it is deliberately not modelled
    // here: wb computes the proof from uploaded bytes and only ever sends it. Do not add it.
}
