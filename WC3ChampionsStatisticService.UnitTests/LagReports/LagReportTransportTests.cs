using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;

namespace WC3ChampionsStatisticService.Tests.LagReports;

/// <summary>
/// Phase 2 / Task 2.3 — verifies that the new <see cref="ConnectionTopologyDto.Transport"/>
/// field is validated by the DTO's <see cref="RegularExpressionAttribute"/> and persisted
/// onto <see cref="LagReportPlayer.Transport"/> through the controller's mapping +
/// repository round-trip.
/// </summary>
[TestFixture]
public class LagReportTransportTests : IntegrationTestBase
{
    private LagReportRepository _repo;

    [SetUp]
    public void SetUp()
    {
        _repo = new LagReportRepository(MongoClient);
    }

    private static LagReportSubmissionDto CreateValidDto(string transport) => new()
    {
        Diagnostics = new DiagnosticsDataDto
        {
            GameId = 1001,
            PlayerId = 1,
            LagEvents = [],
            TargetMtr = [],
            AllServerBaselines = [],
            ReverseMtr = [],
            PingHistory = [],
            ConnectionEvents = [],
        },
        GameMetadata = new GameMetadataDto
        {
            GameId = 5001,
            FloGameId = 1001,
            GameName = "transport-test-game",
            MapPath = "(2)EchoIsles.w3x",
        },
        ConnectionTopology = new ConnectionTopologyDto
        {
            ServerNodeId = 1,
            ServerNodeName = "EU West",
            ConnectionType = EConnectionType.Direct,
            ClientIp = "203.0.113.1",
            Transport = transport,
        },
        IsExplicit = false,
        Categories = [],
        FreeText = "",
        Annotations = [],
    };

    [Test]
    public async Task Posting_LagReport_With_Transport_Persists_The_Value()
    {
        // Arrange: a submission with Transport = "QUIC".
        var dto = CreateValidDto("QUIC");

        // The controller's static helpers are the integration seam: they encapsulate
        // exactly the DTO→domain transformation that SubmitReport performs before
        // calling the repository. Mirrors the existing controller-against-repo pattern
        // used by LagReportRepositoryTests + PlayerMatchTelemetryEndToEndTests.
        Assert.IsNull(LagReportController.ValidateSubmission(dto));
        var player = LagReportController.MapToPlayer(dto, "TransportTester#0001");

        var template = new LagReport
        {
            GameId = dto.GameMetadata.GameId,
            FloGameId = dto.GameMetadata.FloGameId,
            GameName = dto.GameMetadata.GameName,
            MapPath = dto.GameMetadata.MapPath,
            ServerNodeId = dto.ConnectionTopology.ServerNodeId,
            ServerNodeName = dto.ConnectionTopology.ServerNodeName,
        };

        // Act: persist and fetch back.
        var reportId = await _repo.UpsertPlayerData(template.FloGameId, player, template);
        var fetched = await _repo.GetById(reportId);

        // Assert: the QUIC value survives the BSON round-trip.
        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched.Players, Has.Count.EqualTo(1));
        Assert.That(fetched.Players[0].Transport, Is.EqualTo("QUIC"));
    }

    [Test]
    public void Posting_LagReport_With_Invalid_Transport_Returns_400()
    {
        // Arrange: same DTO shape but Transport = "UDP" — outside the allowed regex.
        var dto = CreateValidDto("UDP");

        // Act: drive ASP.NET's DataAnnotations validation (this is exactly what
        // [ApiController] invokes against the model when SubmitReport's parameter
        // binds). A failed Validator.TryValidateObject is what produces the 400
        // BadRequest response that the controller never even sees.
        var ctx = new ValidationContext(dto.ConnectionTopology);
        var errors = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(dto.ConnectionTopology, ctx, errors, validateAllProperties: true);

        // Assert: ModelState would be invalid → ASP.NET returns 400 BadRequest.
        Assert.That(ok, Is.False, "Validator should reject Transport=UDP");
        Assert.That(errors, Has.Some.Matches<ValidationResult>(e =>
            e.ErrorMessage != null && e.ErrorMessage.Contains("Transport")));
    }
}
