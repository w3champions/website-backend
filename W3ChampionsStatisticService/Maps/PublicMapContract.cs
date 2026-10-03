using System.Linq;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// The map shape the anonymous <c>GET api/maps/tournaments</c> route serves: an allowlist of exactly the
/// <see cref="MapContract"/> fields that route returned before temporary maps existed. Fields added to
/// <see cref="MapContract"/> for the admin Maps page (uploader battleTag, temporary-map state, stored path)
/// therefore never reach anonymous callers, whatever matchmaking includes in its answer.
/// </summary>
public class PublicMapContract
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }
    public int MaxTeams { get; set; }
    public MapForce[] MappedForces { get; set; }
    public GameMap GameMap { get; set; }
    public int teamSize { get; set; }
    public bool Disabled { get; set; }

    public static PublicMapContract From(MapContract map) => map == null
        ? null
        : new PublicMapContract
        {
            Id = map.Id,
            Name = map.Name,
            Category = map.Category,
            MaxTeams = map.MaxTeams,
            MappedForces = map.MappedForces,
            GameMap = map.GameMap,
            teamSize = map.teamSize,
            Disabled = map.Disabled,
        };
}

/// <summary><see cref="GetMapsResponse"/> projected to <see cref="PublicMapContract"/> rows.</summary>
public class PublicMapsResponse
{
    public int Total { get; set; }
    public PublicMapContract[] Items { get; set; }

    public static PublicMapsResponse From(GetMapsResponse response) => response == null
        ? null
        : new PublicMapsResponse
        {
            Total = response.Total,
            Items = response.Items?.Select(PublicMapContract.From).ToArray(),
        };
}
