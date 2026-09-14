using System;
using System.IO;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// A local server fault while spooling a temporary-map upload to disk: the spool directory cannot be
/// created or is a link, or the spool file cannot be created, written or closed (a full disk, for
/// example). The client did nothing wrong, and Appendix A.3 defines no body for this, so callers
/// answer a bare 500.
/// <para>
/// Deliberately not an <see cref="IOException"/>, so it is never mistaken for a failed request body,
/// and not a <see cref="TemporaryMapUploadException"/>. The message names the failed step only; the
/// inner exception carries the file-system detail (a random spool path, never a proof or token).
/// </para>
/// </summary>
public class TemporaryMapSpoolException(string message, Exception innerException) : Exception(message, innerException);
