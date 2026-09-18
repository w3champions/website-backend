using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;

namespace W3C.Domain.MatchmakingService.Contracts;

/// <summary>
/// The body of matchmaking's admin <c>POST /maps</c> and <c>PUT /maps/{id}</c>: exactly the fields an admin edits
/// (the fields these routes carried before temporary maps existed, plus the uploader). <see cref="MapContract"/> also
/// models matchmaking-owned temporary-map fields (Path, Temporary, FileState, LastHostedAt, SlotCount,
/// OriginalFileName) so the admin listing can show them; they are read, never written back through these routes.
/// </summary>
internal sealed class AdminMapWriteRequest
{
    public int Id { get; init; }
    public string Name { get; init; }
    public string Category { get; init; }
    public int MaxTeams { get; init; }
    public MapForce[] MappedForces { get; init; }
    public GameMap GameMap { get; init; }
    public int teamSize { get; init; }
    public bool Disabled { get; init; }
    public string Uploader { get; init; }

    public static AdminMapWriteRequest From(MapContract map) => new()
    {
        Id = map.Id,
        Name = map.Name,
        Category = map.Category,
        MaxTeams = map.MaxTeams,
        MappedForces = map.MappedForces,
        GameMap = map.GameMap,
        teamSize = map.teamSize,
        Disabled = map.Disabled,
        Uploader = map.Uploader,
    };
}
