using System.Text.Json;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Submission;

public static class SubmissionJson
{
    /// <summary>C5 wire format: snake_case names; unknown members ignored (System.Text.Json default).</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}
