using System.Collections.Generic;

namespace W3ChampionsStatisticService.Matches;

/// <summary>
/// Matches whose displayed map name is <see cref="MapName"/>. A match is kept when its map id is in
/// <see cref="MapIds"/> (ids whose current name is <see cref="MapName"/>), or when its stored name is
/// <see cref="MapName"/> and its map id is not in <see cref="MapIdsWithCurrentName"/> (ids that display a
/// current name, so their stored name is not what is shown).
/// </summary>
public record MapNameFilter(string MapName, IReadOnlyCollection<int> MapIds, IReadOnlyCollection<int> MapIdsWithCurrentName);
