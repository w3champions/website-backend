using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace W3ChampionsStatisticService.Services.Tracing;

/// <summary>
/// Removes machine IDs from request paths and URLs before they reach traces, logs or telemetry. The only route that
/// carries a machine ID in its path is the ban route ".../bans/machine-ids/{machineId}/accounts", incoming
/// (api/admin/bans/...) and outgoing to matchmaking (admin/bans/...). [NoTrace] keeps the value out of WB's own
/// method spans; this covers what the HTTP instrumentation and the framework record about the request itself.
/// </summary>
public static class MachineIdRedaction
{
    public const string Placeholder = "REDACTED";
    private const string Marker = "/bans/machine-ids/";

    // ASP.NET Core routing is case-insensitive, so the marker is too. A segment starting with "{" is a route template.
    private static readonly Regex MachineIdSegment = new(@"(/bans/machine-ids/)(?!\{)[^/?#]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Redact(string value) =>
        string.IsNullOrEmpty(value) || value.IndexOf(Marker, StringComparison.OrdinalIgnoreCase) < 0
            ? value
            : MachineIdSegment.Replace(value, "$1" + Placeholder);

    /// <summary>Rewrites every string tag (url.path, url.full, ...) and the display name of an HTTP span.</summary>
    public static void RedactTags(Activity activity)
    {
        if (activity == null) return;
        activity.DisplayName = Redact(activity.DisplayName);
        var tags = activity.TagObjects
            .Where(tag => tag.Value is string)
            .Select(tag => (tag.Key, Value: (string)tag.Value))
            .ToList();
        foreach (var (key, value) in tags)
        {
            var redacted = Redact(value);
            if (!ReferenceEquals(redacted, value)) activity.SetTag(key, redacted);
        }
    }
}
