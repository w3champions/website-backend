using System;

namespace W3ChampionsStatisticService.Maps;

/// <summary>File-name rules for temporary maps (design spec §6.4).</summary>
public static class TemporaryMapNaming
{
    public static bool TryGetExtension(string originalFileName, out string extension)
    {
        extension = null;
        var name = originalFileName ?? string.Empty;
        if (name.EndsWith(".w3x", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".w3x";
        }
        else if (name.EndsWith(".w3m", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".w3m";
        }

        return extension != null;
    }
}
