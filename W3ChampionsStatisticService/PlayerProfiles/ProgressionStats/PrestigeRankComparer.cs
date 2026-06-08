namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// Pure, config-free ordering of published progression-rank snapshots.
// Encoding (authoritative): lower League = higher rank (GrandMaster 0 .. Grass 8);
// lower Division = better (I=1 .. IV=4); higher Points = better; apexPoints (when set) ranks above
// any league/division rank, higher = better.
public static class PrestigeRankComparer
{
    // True iff `candidate` is a strictly higher rank than `current`.
    // A null reference for either argument is treated identically to a snapshot with League == null
    // (i.e. not a placed rank): a null candidate is never higher, and any placed candidate beats a null current.
    public static bool IsHigher(PeakRank candidate, PeakRank current)
    {
        if (candidate?.League == null) return false; // not a placed rank
        if (current?.League == null) return true;     // any placed rank beats none

        var candidateIsApex = candidate.ApexPoints != null;
        var currentIsApex = current.ApexPoints != null;
        if (candidateIsApex != currentIsApex) return candidateIsApex; // apex outranks any league/division rank

        if (candidateIsApex)
        {
            if (candidate.ApexPoints != current.ApexPoints) return candidate.ApexPoints > current.ApexPoints;
            return candidate.League < current.League; // tie: GrandMaster(0) over Master(1)
        }

        if (candidate.League != current.League) return candidate.League < current.League;
        if (candidate.Division != current.Division) return candidate.Division < current.Division;
        return candidate.Points > current.Points;
    }
}
