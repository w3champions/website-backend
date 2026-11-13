using System.Collections.Generic;

namespace W3ChampionsStatisticService.Ladder;

/// <summary>
/// Where a player sits on one ladder — the whole of what ordering a search by ladder standing needs.
/// </summary>
/// <remarks>
/// Deliberately narrower than <see cref="Rank"/>: the search core reads RankingPoints and nothing
/// else, so hydrating the joined PlayerOverview on every hit would be payload it never opens.
/// The row set is identical to what <c>LoadRanksForPlayers</c> returns for the same arguments —
/// including dropping ranks whose PlayerOverview is missing — so both stages of a ladder search agree
/// on who counts as ranked.
/// </remarks>
public class PlayerLadderStanding
{
    // Every member of the ranked entry, so ordering can credit the standing to all of them.
    public List<string> MemberIds { get; set; } = [];

    // Ranking points are the ladder's global ordering, monotone across every league and division.
    // The neighbouring fields order nothing: league ids follow creation order (divisions added
    // mid-season take the next free id) and rank numbers restart at 1 in every league.
    public double RankingPoints { get; set; }
}
