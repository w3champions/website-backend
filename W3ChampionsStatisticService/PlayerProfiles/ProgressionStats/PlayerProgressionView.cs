namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// The progression rank as served to clients on the Rank / PlayerGameModeStatPerGateway DTOs.
// Carries only the four published fields (no keying/identity fields). Null when the player has
// no placed progression record for that (season, mode, ...). A record exists only for a placed
// player, so league/division/points are always present; apexPoints is apex-only (nullable).
public class PlayerProgressionView
{
    public int League { get; set; }
    public int Division { get; set; }
    public int Points { get; set; }
    public int? ApexPoints { get; set; }

    public static PlayerProgressionView FromReadModel(PlayerProgression progression)
    {
        if (progression?.League == null || progression.Division == null || progression.Points == null)
        {
            return null;
        }

        return new PlayerProgressionView
        {
            League = progression.League.Value,
            Division = progression.Division.Value,
            Points = progression.Points.Value,
            ApexPoints = progression.ApexPoints,
        };
    }
}
