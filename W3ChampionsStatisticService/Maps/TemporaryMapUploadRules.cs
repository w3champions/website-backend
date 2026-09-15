using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Maps;

namespace W3ChampionsStatisticService.Maps;

/// <summary>The pure decisions of <see cref="TemporaryMapUploadService"/>: layout plausibility, the forwarded capture, the record's GameMap, what may be logged.</summary>
internal static class TemporaryMapUploadRules
{
    private const string FreeLobby = "free";
    private const string MappedForcesLobby = "mapped-forces";

    /// <summary>
    /// Teams 0..23 are player teams; 24 is the observers, which the launcher never emits as a force (§4.4). So a force's
    /// team is below this, and maxTeams — the number of player teams — is at most this (matchmaking takes 1..24; S2-4).
    /// </summary>
    private const int TeamCount = 24;

    /// <summary>The 24 lobby colours, <see cref="Color.RED"/> to <see cref="Color.PEANUT"/>.</summary>
    private const int ColorCount = 24;

    /// <summary>
    /// What the log renders in place of a supplied string that could forge a log line: the file sink renders strings
    /// raw, so a launcherVersion that is not a plain version string (S-L4) and an upstream path, fileState or sha1 that
    /// is not the shape the service expects (S2-2) are replaced by this.
    /// </summary>
    public const string InvalidForLog = "invalid";

    /// <summary>What the log renders for a launcherVersion that is not a plain version string (S-L4).</summary>
    public const string InvalidLauncherVersion = InvalidForLog;

    /// <summary>The longest plain token (a launcherVersion, a fileState) the log renders as sent.</summary>
    private const int MaxTokenLength = 32;

    /// <summary>
    /// §6.3 step 6: the capture must describe a lobby this map can have — a known lobby mode, 1..12 (12-player maps) or
    /// 1..24 slots, 1..24 teams, forces exactly when mapped, every force on a distinct team below the observers,
    /// every seat (human or computer) a distinct in-range index, and every colour, race and difficulty a value WC3 has
    /// (S-M3). Any missing part of the capture is a rejection rather than an exception.
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
        if (capture.SlotCount < 1 || capture.SlotCount > maxSlots
            || capture.MaxTeams < 1 || capture.MaxTeams > TeamCount
            || isFree != (forces.Length == 0))
        {
            return false;
        }

        var seats = new HashSet<int>();
        var teams = new HashSet<int>();
        foreach (var force in forces)
        {
            if (force?.Slots == null || force.Team < 0 || force.Team >= TeamCount || !teams.Add(force.Team))
            {
                return false;
            }

            foreach (var slot in force.Slots)
            {
                if (slot == null || !IsSeat(slot.Index, capture.SlotCount, seats) || (slot.Color != null && !IsColor(slot.Color.Value)))
                {
                    return false;
                }
            }

            foreach (var computer in force.Computers ?? [])
            {
                if (computer == null
                    || !IsSeat(computer.Slot, capture.SlotCount, seats)
                    || !IsColor((int)computer.Color)
                    || computer.Race is not (Race.RnD or Race.HU or Race.OC or Race.NE or Race.UD)
                    || computer.Difficulty is not (Computer.EASY or Computer.NORMAL or Computer.INSANE))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The forces as matchmaking must receive them: fresh copies with a null computers list replaced by an empty one,
    /// because the client's serializer drops null members and matchmaking treats a missing list as "unknown", not
    /// "none". Only meaningful after <see cref="IsPossibleLayout"/> accepted the capture.
    /// </summary>
    public static MapForce[] ForwardedForces(MapForce[] forces)
        => forces.Select(force => new MapForce
        {
            Team = force.Team,
            Slots = force.Slots,
            Computers = force.Computers ?? [],
        }).ToArray();

    /// <summary>
    /// Copies the parsed metadata onto a fresh GameMap and grafts the path on: update-service's MapMetaData has no path
    /// field, but matchmaking and flo both need one (<see cref="TemporaryMapNaming.GameMapPath"/>).
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
        Path = TemporaryMapNaming.GameMapPath(fileKey),
    };

    /// <summary>
    /// The client-supplied launcherVersion is logged only when it is 1..32 characters of [0-9A-Za-z.+-] (S-L4): the
    /// file sink renders strings raw, so anything else — a line break, a control character, 64 KiB of text — is
    /// replaced by <see cref="InvalidLauncherVersion"/>.
    /// </summary>
    public static string LauncherVersionForLog(string launcherVersion)
        => IsPlainToken(launcherVersion, ".+-") ? launcherVersion : InvalidLauncherVersion;

    /// <summary>
    /// A fileState matchmaking supplied, as the log may render it: the unknown value is the point of the warning that
    /// logs it, so it is kept when it is 1..32 characters of [0-9A-Za-z_-], else <see cref="InvalidForLog"/> (S2-2).
    /// </summary>
    public static string LoggableFileState(string fileState)
        => IsPlainToken(fileState, "_-") ? fileState : InvalidForLog;

    /// <summary>
    /// A sha1 an upstream service supplied, as the log may render it: only when it is exactly 40 lowercase hex characters,
    /// the shape the clients require of their own keys, else <see cref="InvalidForLog"/> (S2-2).
    /// </summary>
    public static string LoggableSha1(string sha1)
        => TemporaryMapKeys.IsLowercaseHex(sha1, TemporaryMapKeys.Sha1HexLength) ? sha1 : InvalidForLog;

    /// <summary>
    /// A matchmaking-supplied path as the log may render it: the path itself when it is a temporary map file path without
    /// a control character, else <see cref="InvalidForLog"/> (S2-2). Paths are wb-generated in normal operation and reach a
    /// log line only through matchmaking drift, which is exactly when they cannot be trusted to stay on one line.
    /// </summary>
    public static string LoggablePath(string path)
        => TemporaryMapKeys.IsFilePath(path) && !TemporaryMapNaming.ContainsControlCharacter(path) ? path : InvalidForLog;

    /// <summary>
    /// 1..32 ASCII letters, digits or <paramref name="punctuation"/>. A character loop rather than a regex: .NET's "$"
    /// also matches before a trailing line feed.
    /// </summary>
    private static bool IsPlainToken(string value, string punctuation)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxTokenLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && !punctuation.Contains(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSeat(int index, int slotCount, HashSet<int> seats)
        => index >= 0 && index < slotCount && seats.Add(index);

    private static bool IsColor(int color) => color >= 0 && color < ColorCount;
}
