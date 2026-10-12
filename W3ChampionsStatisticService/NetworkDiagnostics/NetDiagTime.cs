using System;

namespace W3ChampionsStatisticService.NetworkDiagnostics;

public static class NetDiagTime
{
    /// <summary>
    /// Every instant in this feature is UTC (A5). A value without an offset (Kind=Unspecified: an ISO-8601 query value
    /// without "Z", or a stored instant read back without its kind) is taken as UTC rather than as server-local time;
    /// a Local one is converted.
    /// </summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
