using System.Collections.Generic;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;

namespace W3C.Domain.MatchmakingService.Contracts;

/// <summary>The two values matchmaking uses for MapContract.FileState on temporary maps.</summary>
public static class TemporaryMapFileStates
{
    public const string Present = "present";
    public const string Deleted = "deleted";
}

/// <summary>Every temporary-map route wraps its record as { "map": ... } (design spec Appendix A.5).</summary>
public class TemporaryMapEnvelope
{
    public MapContract Map { get; set; }
}

/// <summary>
/// GET /maps/temporary/by-proof-hash/{proofHash}. Status only, never an identifier, so the answer
/// can be relayed to the player verbatim as the pre-check state (design spec §5.4, least privilege).
/// </summary>
public class TemporaryMapStateResponse
{
    public string FileState { get; set; }
}

/// <summary>
/// POST /maps/temporary/verify-proof. Non-mutating; returns identifiers and is therefore consumed
/// only server-side, never forwarded to a client.
/// </summary>
public class VerifyTemporaryMapProofResponse
{
    public int MapId { get; set; }
    public string Path { get; set; }
    public string FileState { get; set; }
}

/// <summary>POST /maps/temporary body (design spec Appendix A.5). matchmaking derives proofHash itself.</summary>
public class CreateTemporaryMapRequest
{
    public string Sha1 { get; set; }
    public string Uploader { get; set; }
    public string OriginalFileName { get; set; }

    /// <summary>SECRET — never log it (§10.3).</summary>
    public string MapProof { get; set; }

    public int MaxTeams { get; set; }
    public int SlotCount { get; set; }

    /// <summary>"free" or "mapped-forces".</summary>
    public string LobbyMode { get; set; }

    public MapForce[] MappedForces { get; set; } = [];
    public GameMap GameMap { get; set; }
}

/// <summary>POST /maps/temporary/{id}/file-restored body. matchmaking re-verifies both values.</summary>
public class TemporaryMapFileRestoredRequest
{
    public string Sha1 { get; set; }
    public string Uploader { get; set; }

    /// <summary>SECRET — never log it (§10.3).</summary>
    public string MapProof { get; set; }
}

/// <summary>201 means we created the record; 409 means an equal-sha1 record already existed and wins.</summary>
public class CreateTemporaryMapResult
{
    public bool Created { get; set; }
    public MapContract Map { get; set; }
}

public class ExpiredTemporaryMapsResponse
{
    public List<ExpiredTemporaryMap> Items { get; set; } = [];
}

public class ExpiredTemporaryMap
{
    public int Id { get; set; }
    public string Path { get; set; }
}
