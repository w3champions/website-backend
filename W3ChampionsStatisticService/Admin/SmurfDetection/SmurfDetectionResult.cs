namespace W3ChampionsStatisticService.Admin.SmurfDetection;

public class SmurfDetectionResult
{
    public string battleTag { get; set; }

    // Includes the requested battleTag itself
    public string[] connectedBattleTags { get; set; }

    public ExplanationStep[] explanation { get; set; }
}
