using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.Maps;

/// <summary>The pure decisions of <see cref="TemporaryMapUploadService"/>: layout plausibility, the forwarded capture, the record's GameMap, what may be logged.</summary>
internal static class TemporaryMapUploadRules
{
    private const string FreeLobby = "free";
    private const string MappedForcesLobby = "mapped-forces";

    /// <summary>Teams 0..23 are player teams; 24 is the observers, which the launcher never emits as a force (§4.4).</summary>
    private const int TeamCount = 24;

    /// <summary>The 24 lobby colours, <see cref="Color.RED"/> to <see cref="Color.PEANUT"/>.</summary>
    private const int ColorCount = 24;

    /// <summary>What the log renders for a launcherVersion that is not a plain version string (S-L4).</summary>
    public const string InvalidLauncherVersion = "invalid";

    private const int MaxLauncherVersionLength = 32;

    /// <summary>
    /// §6.3 step 6: the capture must describe a lobby this map can have — a known lobby mode, 1..12 (12-player maps) or
    /// 1..24 slots, at least one team, forces exactly when mapped, every force on a distinct team below the observers,
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
        if (capture.SlotCount < 1 || capture.SlotCount > maxSlots || capture.MaxTeams < 1 || isFree != (forces.Length == 0))
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
    /// replaced by <see cref="InvalidLauncherVersion"/>. A character loop rather than a regex: .NET's "$" also matches
    /// before a trailing line feed.
    /// </summary>
    public static string LauncherVersionForLog(string launcherVersion)
    {
        if (string.IsNullOrEmpty(launcherVersion) || launcherVersion.Length > MaxLauncherVersionLength)
        {
            return InvalidLauncherVersion;
        }

        foreach (var c in launcherVersion)
        {
            var allowed = c is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '.' or '+' or '-';
            if (!allowed)
            {
                return InvalidLauncherVersion;
            }
        }

        return launcherVersion;
    }

    private static bool IsSeat(int index, int slotCount, HashSet<int> seats)
        => index >= 0 && index < slotCount && seats.Add(index);

    private static bool IsColor(int color) => color >= 0 && color < ColorCount;
}
