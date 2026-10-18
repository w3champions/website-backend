using System;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// A matchmaking map carries a game path ("maps\W3Champions\x.w3x"), update-service stores the file as
/// "W3Champions/x.w3x". This is the inverse of the website's <c>toGameMapPath</c>
/// (src/components/admin/maps/mapFilePath.ts), which prefixes "maps\" and turns "/" into "\".
/// </summary>
public static class MapFilePaths
{
    private const string GamePathPrefix = "maps/";

    /// <summary>Drops leading slashes and exactly one leading "maps" segment and uses forward slashes. Case is kept. Null when there is no file name.</summary>
    public static string ToUpdateServiceFilePath(string gameMapPath)
    {
        if (string.IsNullOrWhiteSpace(gameMapPath)) return null;

        var path = gameMapPath.Trim().Replace('\\', '/').TrimStart('/');
        if (path.StartsWith(GamePathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[GamePathPrefix.Length..];
        }

        return path.Length == 0 ? null : path;
    }
}
