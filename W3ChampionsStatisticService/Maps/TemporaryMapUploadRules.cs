using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.Maps;

/// <summary>The pure decisions of <see cref="TemporaryMapUploadService"/>: layout plausibility, the record's GameMap, failure classes.</summary>
internal static class TemporaryMapUploadRules
{
    private const string FreeLobby = "free";
    private const string MappedForcesLobby = "mapped-forces";

    /// <summary>
    /// §6.3 step 6: the capture must describe a lobby this map can have — a known lobby mode, 1..12 (12-player maps) or
    /// 1..24 slots, at least one team, forces exactly when mapped, and every seat a distinct in-range index. Any missing
    /// part of the capture is a rejection rather than an exception.
    /// </summary>
    public static bool IsPossibleLayout(TemporaryMapCapture capture, GameMap parsed)
    {
        var forces = capture?.MappedForces;
        var isFree = capture?.LobbyMode == FreeLobby;
        if (forces == null || !(isFree || capture.LobbyMode == MappedForcesLobby))
        {
            return false;
        }

        var maxSlots = parsed.TwelveP ? 12 : 24;
        if (capture.SlotCount < 1 || capture.SlotCount > maxSlots || capture.MaxTeams < 1 || isFree != (forces.Length == 0))
        {
            return false;
        }

        var seen = new HashSet<int>();
        foreach (var force in forces)
        {
            if (force?.Slots == null)
            {
                return false;
            }

            foreach (var slot in force.Slots)
            {
                if (slot == null || slot.Index < 0 || slot.Index >= capture.SlotCount || !seen.Add(slot.Index))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Copies the parsed metadata onto a fresh GameMap and grafts the path on: update-service's MapMetaData has no path
    /// field, but matchmaking and flo both need one, and it is exactly the fileKey with backslashes under a "maps\" root.
    /// </summary>
    public static GameMap ToGameMap(GameMap parsed, string fileKey) => new()
    {
        Sha1 = parsed.Sha1?.ToLowerInvariant(),
        Checksum = parsed.Checksum,
        Crc32 = parsed.Crc32,
        Name = parsed.Name,
        Author = parsed.Author,
        Description = parsed.Description,
        Width = parsed.Width,
        Height = parsed.Height,
        SuggestedPlayers = parsed.SuggestedPlayers,
        NumPlayers = parsed.NumPlayers,
        Players = parsed.Players ?? [],
        Forces = parsed.Forces ?? [],
        TwelveP = parsed.TwelveP,
        Path = @"maps\" + fileKey.Replace('/', '\\'),
    };

    /// <summary>
    /// matchmaking definitely did not write: it answered a non-2xx status. On create a 409 is not definite — the client
    /// reads a well-formed 409 itself, so a 409 that still throws broke the contract and may hide a written record.
    /// </summary>
    public static bool IsDefiniteFailure(Exception ex, bool conflictIsAmbiguous)
        => ex is HttpRequestException { StatusCode: { } status }
           && (int)status is < 200 or > 299
           && !(conflictIsAmbiguous && status == HttpStatusCode.Conflict);

    /// <summary>The write may have happened: no status (transport, HttpClient timeout) or an answer that breaks the contract.</summary>
    public static bool IsAmbiguousFailure(Exception ex, bool conflictIsAmbiguous)
        => ex is OperationCanceledException || (ex is HttpRequestException && !IsDefiniteFailure(ex, conflictIsAmbiguous));
}
