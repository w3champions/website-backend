using System;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// A terminal outcome of a temporary-map upload, carrying the exact status code and body pinned by
/// design spec Appendix A.3. The message is the code only — never let a proof reach an exception.
/// </summary>
public class TemporaryMapUploadException(int statusCode, string code, object body = null) : Exception(code)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;

    /// <summary>The response body, e.g. <c>{ code: "FILE_TOO_LARGE" }</c>.</summary>
    public object Body { get; } = body ?? new { code };
}
