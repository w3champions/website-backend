namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

/// <summary>
/// Authoritative encoding of a progression league. Lower value = higher rank
/// (<see cref="GrandMaster"/> = 0 .. <see cref="Grass"/> = 8). <see cref="GrandMaster"/> and
/// <see cref="Master"/> are the apex leagues. This mirrors the encoding documented on
/// <see cref="PrestigeRankComparer"/>; the published <c>league</c> field is the integer value below.
/// </summary>
public enum ProgressionLeague
{
    GrandMaster = 0,
    Master = 1,
    Adept = 2,
    Diamond = 3,
    Platinum = 4,
    Gold = 5,
    Silver = 6,
    Bronze = 7,
    Grass = 8,
}
