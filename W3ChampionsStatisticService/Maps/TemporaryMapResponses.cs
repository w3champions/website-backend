namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// The success body of POST api/maps/temporary (design spec Appendix A.3). It carries NO secret:
/// the uploader already holds the bytes and computes the proof locally, so there is nothing to hand
/// back (§5.6 — the entitled surfaces are the lobby messages and FLO_TV_WATCH_AUTH_RES, never this).
/// </summary>
public class TemporaryMapUploadResponse
{
    public int MapId { get; set; }

    /// <summary>The canonical fileKey, e.g. "W3Champions/CustomGames/Legion TD-94ec3bda.w3x".</summary>
    public string Path { get; set; }

    public string Name { get; set; }

    public string Sha1 { get; set; }
}

/// <summary>201 for a newly created record, 200 for a dedupe hit or a restore.</summary>
public class TemporaryMapUploadOutcome
{
    public bool Created { get; set; }

    public TemporaryMapUploadResponse Response { get; set; }
}
