using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Ports;
using WC3ChampionsStatisticService.Tests.Admin.Jobs;

namespace WC3ChampionsStatisticService.Tests.Ranks;

[TestFixture]
public class RankMemberIdsBackfillJobTests
{
    [Test]
    public async Task RunAsync_BackfillsEverySeason_AndCheckpointsTheLast()
    {
        var repository = new Mock<IRankRepository>();
        repository.Setup(r => r.LoadRankSeasons()).ReturnsAsync(new List<int> { 12, 13 });
        repository.Setup(r => r.BackfillMemberIds(12)).ReturnsAsync(3);
        repository.Setup(r => r.BackfillMemberIds(13)).ReturnsAsync(2);
        var context = new FakeAdminJobContext();

        await new RankMemberIdsBackfillJob(repository.Object).RunAsync(context, CancellationToken.None);

        repository.Verify(r => r.BackfillMemberIds(12), Times.Once);
        repository.Verify(r => r.BackfillMemberIds(13), Times.Once);
        Assert.AreEqual(5, context.ItemsProcessed);
        Assert.AreEqual(2, context.Reports[^1].Current);
        Assert.AreEqual(2, context.Reports[^1].Total);
        Assert.AreEqual(13, context.LastCheckpoint["LastCompletedSeason"].AsInt32);
        // Paced after every season, so cancellation and back-pressure reach the job between batches
        Assert.AreEqual(2, context.Paces);
    }

    [Test]
    public async Task RunAsync_ResumesPastTheCheckpointedSeasons()
    {
        var repository = new Mock<IRankRepository>();
        repository.Setup(r => r.LoadRankSeasons()).ReturnsAsync(new List<int> { 12, 13 });
        repository.Setup(r => r.BackfillMemberIds(13)).ReturnsAsync(2);
        var context = new FakeAdminJobContext(new BsonDocument("LastCompletedSeason", 12));

        await new RankMemberIdsBackfillJob(repository.Object).RunAsync(context, CancellationToken.None);

        // Seasons at or below the checkpoint stay done — a filled row never loses the field
        repository.Verify(r => r.BackfillMemberIds(12), Times.Never);
        repository.Verify(r => r.BackfillMemberIds(13), Times.Once);
        // The skipped season still counts as done, so progress reads 2/2 rather than restarting
        Assert.AreEqual(2, context.Reports[^1].Current);
        Assert.AreEqual(2, context.Reports[^1].Total);
    }

    [Test]
    public async Task RunAsync_WithNothingPastTheCheckpoint_DoesNoWork()
    {
        var repository = new Mock<IRankRepository>();
        repository.Setup(r => r.LoadRankSeasons()).ReturnsAsync(new List<int> { 12, 13 });
        var context = new FakeAdminJobContext(new BsonDocument("LastCompletedSeason", 13));

        await new RankMemberIdsBackfillJob(repository.Object).RunAsync(context, CancellationToken.None);

        repository.Verify(r => r.BackfillMemberIds(It.IsAny<int>()), Times.Never);
    }
}
