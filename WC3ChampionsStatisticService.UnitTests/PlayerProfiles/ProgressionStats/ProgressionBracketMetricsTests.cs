using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Prometheus;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.ProgressionStats;

[TestFixture]
public class ProgressionBracketMetricsTests
{
    private static async Task<string> ExportMetricsText()
    {
        using var ms = new MemoryStream();
        await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [Test]
    public async Task Publish_SetsBracketAndTotalGauges()
    {
        ProgressionBracketMetrics.Publish(new List<ProgressionBracketCount>
        {
            new() { GameMode = GameMode.GM_1v1, League = 5, Division = 1, Count = 7 },
            new() { GameMode = GameMode.GM_1v1, League = 5, Division = 2, Count = 3 },
        });

        var text = await ExportMetricsText();
        StringAssert.Contains("progression_bracket_count{gameMode=\"GM_1v1\",league=\"Gold\",division=\"1\"} 7", text);
        StringAssert.Contains("progression_bracket_count{gameMode=\"GM_1v1\",league=\"Gold\",division=\"2\"} 3", text);
        StringAssert.Contains("progression_ranked_total{gameMode=\"GM_1v1\"} 10", text);
    }

    [Test]
    public async Task Publish_RemovesStaleSeries()
    {
        ProgressionBracketMetrics.Publish(new List<ProgressionBracketCount>
        {
            new() { GameMode = GameMode.GM_2v2, League = 6, Division = 1, Count = 4 },
        });
        ProgressionBracketMetrics.Publish(new List<ProgressionBracketCount>
        {
            new() { GameMode = GameMode.GM_2v2, League = 7, Division = 1, Count = 2 },
        });

        var text = await ExportMetricsText();
        StringAssert.DoesNotContain("gameMode=\"GM_2v2\",league=\"Silver\"", text);
        StringAssert.Contains("progression_bracket_count{gameMode=\"GM_2v2\",league=\"Bronze\",division=\"1\"} 2", text);
    }

    [Test]
    public async Task Publish_ApexLeagueWithoutDivision_UsesEmptyDivisionLabel()
    {
        ProgressionBracketMetrics.Publish(new List<ProgressionBracketCount>
        {
            new() { GameMode = GameMode.GM_4v4, League = 1, Division = null, Count = 5 },
        });
        var text = await ExportMetricsText();
        StringAssert.Contains("progression_bracket_count{gameMode=\"GM_4v4\",league=\"Master\",division=\"\"} 5", text);
    }
}
