using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Admin.Jobs;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Ladder;

/// <summary>
/// Fills <see cref="Rank.MemberIds"/> on rows written before the field existed, one season per
/// batch. Rows the sync writes carry the field from birth, so until this has run on an
/// environment the member-list lookups (search ordering, ranks-for-players) miss pre-field rows;
/// country/clan/chat keep working through their two-field fallback either way.
/// </summary>
/// <remarks>
/// The checkpoint is the last completed season. Seasons are processed ascending and a filled row
/// never becomes unfilled — new rows always carry the field — so everything at or below the
/// checkpoint stays done and a resume continues where the run died. The missing-field match in
/// <see cref="IRankRepository.BackfillMemberIds"/> makes redoing a season a no-op, which covers
/// the checkpoint interval an abrupt death can lose.
/// </remarks>
[Trace]
public class RankMemberIdsBackfillJob(IRankRepository rankRepository) : IAdminJob
{
    public string Key => "rank-member-ids-backfill";

    public string Name => "Rank MemberIds backfill";

    public string Description => "Fills the Rank member list (MemberIds) on rows written before the field existed, one season per batch. Run once per environment: until then, pre-existing ranks stay invisible to the search ordering and ranks-for-players lookups. Safe to re-run; resumes from the last completed season.";

    public async Task RunAsync(IAdminJobContext context, CancellationToken cancellationToken)
    {
        var seasons = await rankRepository.LoadRankSeasons();

        var lastCompleted = context.Checkpoint?.GetValue("LastCompletedSeason", int.MinValue).AsInt32;
        var remaining = seasons.Where(s => s > (lastCompleted ?? int.MinValue)).ToList();
        var done = seasons.Count - remaining.Count;

        foreach (var season in remaining)
        {
            var filled = await rankRepository.BackfillMemberIds(season);
            context.AddItems(filled);
            done++;
            await context.Report(done, seasons.Count, $"season {season}: filled {filled} row(s)",
                new BsonDocument("LastCompletedSeason", season));
            await context.Pace(cancellationToken);
        }
    }
}
