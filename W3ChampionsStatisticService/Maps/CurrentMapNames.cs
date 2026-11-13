using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.Matchmaking;
using W3C.Domain.GameModes;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;
using W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// Current matchmaking map names by map id, used at read time. Stored entries keep the name from match
/// time; modes in <see cref="GameModesHelper.CurrentMapNameGameModes"/> display the current name instead,
/// so a rename in matchmaking applies to all past games of that map. Entries without a map id, or with an
/// id matchmaking doesn't know, keep their stored name. Nothing here is ever written back.
/// </summary>
public sealed class CurrentMapNames(IReadOnlyDictionary<int, string> namesById)
{
    public static readonly CurrentMapNames Empty = new(new Dictionary<int, string>());

    private readonly IReadOnlyDictionary<int, string> _namesById = namesById;

    public int Count => _namesById.Count;

    public static CurrentMapNames From(IEnumerable<MapContract> maps)
    {
        var namesById = new Dictionary<int, string>();
        foreach (var map in maps ?? [])
        {
            if (map != null && !string.IsNullOrWhiteSpace(map.Name))
            {
                namesById[map.Id] = map.Name;
            }
        }

        return new CurrentMapNames(namesById);
    }

    /// <summary>True when an entry with this map id displays the current name (in a current-name mode).</summary>
    public bool HasCurrentName(int? mapId)
    {
        return mapId is int id && _namesById.ContainsKey(id);
    }

    public string Resolve(GameMode gameMode, int? mapId, string storedName)
    {
        return GameModesHelper.UsesCurrentMapName(gameMode) ? Resolve(mapId, storedName) : storedName;
    }

    /// <summary>Ignores the game mode: for stats whose entries don't record one.</summary>
    public string Resolve(int? mapId, string storedName)
    {
        return mapId is int id && _namesById.TryGetValue(id, out var name) ? name : storedName;
    }

    /// <summary>
    /// Per mode. The all-modes bucket (<see cref="GameMode.Undefined"/>) has no mode per entry, so an entry
    /// there displays the current name when its map id appears in a current-name mode of the same document.
    /// </summary>
    public void Apply(IEnumerable<MapsPerSeason> mapsPerSeasons)
    {
        foreach (var mapsPerSeason in mapsPerSeasons ?? [])
        {
            var modes = mapsPerSeason?.MatchesOnMapPerModes ?? [];
            var currentNameMapIds = modes
                .Where(m => GameModesHelper.UsesCurrentMapName(m.GameMode))
                .SelectMany(m => m.Maps ?? [])
                .Where(m => m.MapId != null)
                .Select(m => m.MapId.Value)
                .ToHashSet();

            foreach (var mode in modes)
            {
                foreach (var entry in mode.Maps ?? [])
                {
                    var usesCurrentName = mode.GameMode == GameMode.Undefined
                        ? entry.MapId is int id && currentNameMapIds.Contains(id)
                        : GameModesHelper.UsesCurrentMapName(mode.GameMode);
                    if (usesCurrentName)
                    {
                        entry.MapName = Resolve(entry.MapId, entry.MapName);
                    }
                }
            }
        }
    }

    /// <summary>Hero stats entries record no game mode, so every entry with a known map id gets the current name.</summary>
    public void Apply(PlayerHeroStats heroStats)
    {
        foreach (var item in heroStats?.HeroStatsItemList ?? [])
        {
            Apply(item?.Stats);
        }
    }

    /// <summary>Race stats entries record no game mode, so every entry with a known map id gets the current name.</summary>
    public void Apply(PlayerRaceOnMapVersusRaceRatioView raceStats)
    {
        if (raceStats == null) return;

        Apply(raceStats.RaceWinsOnMap);
        foreach (var perPatch in raceStats.RaceWinsOnMapByPatch?.Values ?? Enumerable.Empty<MapWinsPerRaceList>())
        {
            Apply(perPatch);
        }
    }

    /// <summary>These stats are written for 1v1 only.</summary>
    public void Apply(IEnumerable<OverallRaceAndWinStat> raceAndWinStats)
    {
        foreach (var stat in raceAndWinStats ?? [])
        {
            if (stat == null) continue;

            Apply(stat.StatsPerModes);
            foreach (var perPatch in stat.PatchToStatsPerModes?.Values ?? Enumerable.Empty<List<MapToRaceVsRaceRatio>>())
            {
                Apply(perPatch);
            }
        }
    }

    public void Apply(Matchup match)
    {
        if (match == null) return;

        match.MapName = Resolve(match.GameMode, match.MapId, match.MapName);
    }

    public void Apply<T>(IEnumerable<T> matches) where T : Matchup
    {
        foreach (var match in matches ?? [])
        {
            Apply(match);
        }
    }

    public void Apply(MatchupDetail matchupDetail)
    {
        Apply(matchupDetail?.Match);
    }

    private void Apply(MapWinsPerRaceList winsPerRace)
    {
        foreach (var race in winsPerRace ?? [])
        {
            foreach (var entry in race?.WinLossesOnMap ?? [])
            {
                entry.MapName = Resolve(entry.MapId, entry.MapName);
            }
        }
    }

    private void Apply(List<MapToRaceVsRaceRatio> ratios)
    {
        foreach (var entry in ratios ?? [])
        {
            entry.MapName = Resolve(GameMode.GM_1v1, entry.MapId, entry.MapName);
        }
    }
}
