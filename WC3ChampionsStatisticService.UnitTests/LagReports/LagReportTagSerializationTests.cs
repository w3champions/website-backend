using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;

namespace WC3ChampionsStatisticService.Tests.LagReports;

/// <summary>
/// The launcher submits the system-derived connection verdict as a string array
/// under "tags": ["LAN"] / ["LastMile"] / []. These tests pin the exact wire
/// contract: the <see cref="ELagReportTag"/> members must serialize to and
/// deserialize from the literal strings "LAN" and "LastMile" via the shared
/// <see cref="JsonStringEnumListConverter{T}"/> (case-insensitive read).
/// </summary>
[TestFixture]
public class LagReportTagSerializationTests
{
    // A holder so the [JsonConverter] list converter is exercised exactly as it
    // is on the DTO property.
    private sealed class TagHolder
    {
        [System.Text.Json.Serialization.JsonPropertyName("tags")]
        [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumListConverter<ELagReportTag>))]
        public List<ELagReportTag> Tags { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Test]
    public void Tags_serialize_to_exact_wire_strings_LAN_and_LastMile()
    {
        var holder = new TagHolder { Tags = [ELagReportTag.LAN, ELagReportTag.LastMile] };

        var json = JsonSerializer.Serialize(holder, Web);

        // Exact literals — these are the strings the launcher sends/reads.
        Assert.That(json, Does.Contain("\"LAN\""));
        Assert.That(json, Does.Contain("\"LastMile\""));
    }

    [Test]
    public void Tags_deserialize_from_LAN_and_LastMile()
    {
        const string json = """{ "tags": ["LAN", "LastMile"] }""";

        var holder = JsonSerializer.Deserialize<TagHolder>(json, Web);

        Assert.That(holder!.Tags, Is.EqualTo(new List<ELagReportTag>
        {
            ELagReportTag.LAN, ELagReportTag.LastMile,
        }));
    }

    [Test]
    public void Tags_deserialize_is_case_insensitive()
    {
        // The shared list converter uses Enum.TryParse(ignoreCase: true).
        const string json = """{ "tags": ["lan", "lastmile"] }""";

        var holder = JsonSerializer.Deserialize<TagHolder>(json, Web);

        Assert.That(holder!.Tags, Is.EqualTo(new List<ELagReportTag>
        {
            ELagReportTag.LAN, ELagReportTag.LastMile,
        }));
    }

    [Test]
    public void SubmissionDto_deserializes_body_WITH_tags()
    {
        const string json = """
        {
          "is_explicit": false,
          "free_text": "",
          "categories": ["SpikeLag"],
          "tags": ["LAN"]
        }
        """;

        var dto = JsonSerializer.Deserialize<LagReportSubmissionDto>(json, Web);

        Assert.That(dto!.Tags, Is.EqualTo(new List<ELagReportTag> { ELagReportTag.LAN }));
        // Categories still parse independently — the two are distinct fields.
        Assert.That(dto.Categories, Is.EqualTo(new List<EIssueCategory> { EIssueCategory.SpikeLag }));
    }

    [Test]
    public void SubmissionDto_deserializes_body_WITHOUT_tags_DefaultsToEmpty()
    {
        // Older launcher payloads omit "tags" entirely. Must default to an empty
        // list, never null, and never throw.
        const string json = """
        {
          "is_explicit": false,
          "free_text": "",
          "categories": []
        }
        """;

        var dto = JsonSerializer.Deserialize<LagReportSubmissionDto>(json, Web);

        Assert.That(dto!.Tags, Is.Not.Null);
        Assert.That(dto.Tags, Is.Empty);
    }

    [Test]
    public void SubmissionDto_tolerates_unknown_extra_field()
    {
        // System.Text.Json Web defaults ignore unknown members (no
        // [JsonExtensionData] needed) — a forward-compat payload must still bind.
        const string json = """
        {
          "is_explicit": false,
          "tags": ["LastMile"],
          "some_future_field": { "nested": 123 },
          "another_unknown": "ignored"
        }
        """;

        var dto = JsonSerializer.Deserialize<LagReportSubmissionDto>(json, Web);

        Assert.That(dto!.Tags, Is.EqualTo(new List<ELagReportTag> { ELagReportTag.LastMile }));
    }
}
