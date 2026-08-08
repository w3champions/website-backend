using System;
using System.Text.Json;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.Admin.Jobs;

namespace WC3ChampionsStatisticService.Tests.Admin.Jobs;

/// <summary>
/// Pins the wire contract the admin UI's AdminJob type is written against, in the manner of the
/// search suite's SubscriberContractTests: every field the UI reads, by its exact camelCase name
/// and JSON type. The serializer options mirror the service's MVC defaults.
/// </summary>
[TestFixture]
public class AdminJobDtoTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The UI compares the status against its name, so an index here renders a bare number in
    /// the status chip and defeats every comparison — including the ones that speed up polling
    /// while a job runs and add force to a re-run of a completed one.
    /// </summary>
    [Test]
    public void EveryStatus_SerializesAsItsName()
    {
        foreach (var status in Enum.GetValues<AdminJobStatus>())
        {
            var json = JsonSerializer.Serialize(new AdminJobDto { Status = status }, Web);

            StringAssert.Contains($"\"status\":\"{status}\"", json);
        }
    }

    [Test]
    public void SerializesEveryFieldTheAdminUiReads_ByNameAndJsonType()
    {
        var dto = new AdminJobDto
        {
            Key = "rank-member-ids-backfill",
            Name = "Rank MemberIds backfill",
            Description = "desc",
            RequiredPermission = EPermission.Jobs,
            RequiresConfirmation = false,
            Status = AdminJobStatus.Completed,
            Progress = new AdminJobProgress { Current = 14, Total = 14, Message = "season 13" },
            StartedAt = DateTimeOffset.Parse("2026-08-08T17:42:23.927Z"),
            FinishedAt = DateTimeOffset.Parse("2026-08-08T17:42:37.265Z"),
            DurationMs = 13337,
            ItemsProcessed = 173568,
            TriggeredBy = "admin#123",
            RunCount = 1,
            Error = null,
            HasCheckpoint = true,
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, Web));
        var root = doc.RootElement;

        // One invariant, many clauses: Assert.Multiple reports every violated clause in one run
        // instead of stopping at the first.
        Assert.Multiple(() =>
        {
            Assert.AreEqual(JsonValueKind.String, root.GetProperty("key").ValueKind);
            Assert.AreEqual(JsonValueKind.String, root.GetProperty("name").ValueKind);
            Assert.AreEqual(JsonValueKind.String, root.GetProperty("description").ValueKind);
            // The UI's permission enum is numeric, so this one must stay a number.
            Assert.AreEqual(JsonValueKind.Number, root.GetProperty("requiredPermission").ValueKind);
            Assert.AreEqual((int)EPermission.Jobs, root.GetProperty("requiredPermission").GetInt32());
            Assert.AreEqual(JsonValueKind.False, root.GetProperty("requiresConfirmation").ValueKind);
            Assert.AreEqual("Completed", root.GetProperty("status").GetString());

            var progress = root.GetProperty("progress");
            Assert.AreEqual(14, progress.GetProperty("current").GetInt64());
            Assert.AreEqual(14, progress.GetProperty("total").GetInt64());
            Assert.AreEqual("season 13", progress.GetProperty("message").GetString());

            // Dates travel as ISO 8601 strings; the UI hands them to new Date(...).
            Assert.AreEqual(JsonValueKind.String, root.GetProperty("startedAt").ValueKind);
            StringAssert.StartsWith("2026-08-08T17:42:23", root.GetProperty("startedAt").GetString());
            Assert.AreEqual(JsonValueKind.String, root.GetProperty("finishedAt").ValueKind);

            Assert.AreEqual(13337, root.GetProperty("durationMs").GetInt64());
            Assert.AreEqual(173568, root.GetProperty("itemsProcessed").GetInt64());
            Assert.AreEqual("admin#123", root.GetProperty("triggeredBy").GetString());
            Assert.AreEqual(1, root.GetProperty("runCount").GetInt32());
            Assert.AreEqual(JsonValueKind.Null, root.GetProperty("error").ValueKind);
            Assert.AreEqual(JsonValueKind.True, root.GetProperty("hasCheckpoint").ValueKind);
        });
    }
}
