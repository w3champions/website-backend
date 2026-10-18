using System.Collections.Generic;

namespace W3C.Domain.MatchmakingService;

/// <summary>Body of matchmaking <c>POST /maps/game-map-metadata</c>: sets gameMap.checksum/crc32 by sha1, never unqueues.</summary>
public class GameMapMetadataUpdateRequest
{
    public List<GameMapMetadataUpdate> Updates { get; set; } = [];
}

public class GameMapMetadataUpdate
{
    /// <summary>40 lowercase hex characters, unique within one request.</summary>
    public string Sha1 { get; set; }
    public long Checksum { get; set; }
    public long Crc32 { get; set; }
}

public class GameMapMetadataUpdateResponse
{
    public List<GameMapMetadataUpdateResult> Results { get; set; }
}

/// <summary>Matched/modified are the Mongo updateMany counts for that sha1.</summary>
public class GameMapMetadataUpdateResult
{
    public string Sha1 { get; set; }
    public int Matched { get; set; }
    public int Modified { get; set; }
}
