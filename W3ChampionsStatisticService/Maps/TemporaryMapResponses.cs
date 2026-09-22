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

/// <summary>
/// The <c>code</c> of every failure body POST api/maps/temporary can answer (Appendix A.3), spelled once: the reader,
/// the service and the controller answer through these constants, so the wire contract cannot drift between them.
/// </summary>
public static class TemporaryMapErrorCodes
{
    public const string Extension = "EXTENSION";
    public const string Metadata = "METADATA";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string Sha1Mismatch = "SHA1_MISMATCH";
    public const string InvalidLayout = "INVALID_LAYOUT";
    public const string ProofMismatch = "PROOF_MISMATCH";
    public const string QuotaExceeded = "QUOTA_EXCEEDED";
    public const string Upstream = "UPSTREAM";
    public const string ParserMismatch = "PARSER_MISMATCH";
    public const string TempMapKeyMismatch = "TEMP_MAP_KEY_MISMATCH";
}

/// <summary>The failure bodies of Appendix A.3, composed in one place. Neither ever carries a secret.</summary>
public static class TemporaryMapFailureBodies
{
    /// <summary><c>{ code }</c>: every A.3 failure but the quota.</summary>
    public static object Coded(string code) => new { code };

    /// <summary><c>{ code: "QUOTA_EXCEEDED", retryAfterSeconds }</c>, the one A.3 body with a second field.</summary>
    public static object QuotaExceeded(int retryAfterSeconds)
        => new { code = TemporaryMapErrorCodes.QuotaExceeded, retryAfterSeconds };
}
