namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// The progression rank as served to clients on the Rank / PlayerGameModeStatPerGateway DTOs.
// Carries only the four published fields (no keying/identity fields). Null when the player has
// no placed progression record for that (season, mode, ...).
public class PlayerProgressionView
{
    public int? League { get; set; }
    public int? Division { get; set; }
    public int? Points { get; set; }
    public int? ApexPoints { get; set; }

    public static PlayerProgressionView FromReadModel(PlayerProgression progression)
    {
        if (progression == null)
        {
            return null;
        }

        return new PlayerProgressionView
        {
            League = progression.League,
            Division = progression.Division,
            Points = progression.Points,
            ApexPoints = progression.ApexPoints,
        };
    }
}
