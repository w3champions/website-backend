using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;

public class PlayerMmrRpTimeline(string battleTag, Race race, GateWay gateWay, int season, GameMode gameMode) : IIdentifiable
{
    public string Id { get; set; } = $"{season}_{battleTag}_@{gateWay}_{race}_{gameMode}";
    public List<MmrRpAtDate> MmrRpAtDates { get; set; } = new List<MmrRpAtDate>();

    /// <summary>
    /// Id of the last match folded into this timeline, so the same match is never
    /// counted twice.
    ///
    /// The read-model pipeline delivers at least once. A handler that throws part
    /// way through a match leaves the players it already wrote committed, and the
    /// event is then retried every few seconds until it succeeds; a crash between
    /// the handler succeeding and the watermark being saved replays the event once
    /// on restart. Both replay the SAME event, which is why remembering only the
    /// last id is sufficient - the watermark cannot advance past a failing event,
    /// so an older one is never replayed after a newer one.
    ///
    /// Without this, <see cref="MmrRpAtDate.MergeSameDay"/> would accumulate the
    /// games count again on every replay, without bound.
    /// </summary>
    [BsonIgnoreIfNull]
    public string LastProcessedMatchId { get; set; }

    /// <summary>
    /// Optimistic concurrency token, bumped on every write.
    /// <para>
    /// This document is written by two independent producers - the live match
    /// handler and the backfill job - and both replace it whole. Without a token
    /// they silently destroy each other's writes: a live write landing second
    /// reverts a day the backfill had just rebuilt, and a backfill write landing
    /// second drops the day the handler had just recorded. Neither is repaired
    /// afterwards, because both writers believe they succeeded.
    /// </para>
    /// <para>
    /// Writers filter on the revision they read and retry when it has moved.
    /// Documents predating this field deserialize as 0, which is a valid starting
    /// revision - no migration is needed.
    /// </para>
    /// </summary>
    [JsonIgnore]
    public int Revision { get; set; }

    /// <summary>
    /// Set by the backfill on every document it rewrites, and cleared in one pass at
    /// the end of a successful run as <see cref="SchemaVersion"/> is raised.
    /// <para>
    /// The backfill works forwards a day at a time, so a document is only fully
    /// rebuilt once the run reaches the end. Raising the version as each day is
    /// written would claim the whole history had the new fields while its later days
    /// still did not; a blanket update at the end would instead sweep in documents the
    /// backfill never touched, whose entries genuinely can't be verified. Marking as we
    /// go and promoting the marks at the end is exact.
    /// </para>
    /// </summary>
    [JsonIgnore]
    [BsonIgnoreIfNull]
    public bool? BackfillPending { get; set; }

    [Trace]
    public void UpdateTimeline(MmrRpAtDate mmrRpAtDate)
    {
        // Empty?
        if (MmrRpAtDates.Count == 0)
        {
            MmrRpAtDates.Add(mmrRpAtDate);
            return;
        }

        // Insert at last pos?
        int index = MmrRpAtDates.Count - 1;
        if (MmrRpAtDates[index].Date <= mmrRpAtDate.Date)
        {
            if (CheckDateExists(index, mmrRpAtDate))
            {
                HandleDateExists(index, mmrRpAtDate);
                return;
            }
            MmrRpAtDates.Add(mmrRpAtDate);
            return;
        }

        // Insert at first pos?
        index = 0;
        {
            if (MmrRpAtDates[index].Date >= mmrRpAtDate.Date)
            {
                if (CheckDateExists(index, mmrRpAtDate))
                {
                    HandleDateExists(index, mmrRpAtDate);
                    return;
                }
                MmrRpAtDates.Insert(index, mmrRpAtDate);
                return;
            }
        }

        int bsIndex = MmrRpAtDates.BinarySearch(mmrRpAtDate);
        if (bsIndex < 0)
            bsIndex = ~bsIndex;

        // Check if date already exists
        // if so, use the mmrRpAtDate with later Date
        for (int i = 0; i <= 1; i++)
        {
            index = bsIndex - i;
            if (index >= 0 && index < MmrRpAtDates.Count)
            {
                if (CheckDateExists(index, mmrRpAtDate))
                {
                    HandleDateExists(index, mmrRpAtDate);
                    return;
                }
            }
        }
        MmrRpAtDates.Insert(bsIndex, mmrRpAtDate);
    }

    private Boolean CheckDateExists(int oldId, MmrRpAtDate mmrRpAtDate)
    {
        var neighbour = MmrRpAtDates[oldId];
        if (mmrRpAtDate.HasSameYearMonthDayAs(neighbour))
        {
            return true;
        }
        return false;
    }

    private void HandleDateExists(int oldId, MmrRpAtDate mmrRpAtDate)
    {
        // Merge rather than replace. Replacing kept only the day's last game,
        // which loses the games count and the intra-day peak.
        MmrRpAtDates[oldId].MergeSameDay(mmrRpAtDate);
    }
}

public class MmrRpAtDate(int mmr, double? rp, DateTimeOffset date, double? rd = null, int? games = null, int? dailyMaxMmr = null) : IComparable
{
    /// <summary>The rating after the last game of the day.</summary>
    public int Mmr { get; set; } = mmr;
    public double? Rp { get; set; } = rp;

    // The three fields below are omitted from BSON whenever they hold their
    // default, because Mongo repeats every field name in every document and
    // these would otherwise cost a few hundred MB across the collection for
    // values that are usually redundant. Absence has a defined meaning, so
    // reads must go through the accessors rather than the raw properties.

    /// <summary>
    /// The highest rating reached during the day, stored only when it differs
    /// from the day's closing Mmr. Mmr alone records where the player finished,
    /// so a spike followed by losses would otherwise never register as a peak.
    /// </summary>
    [BsonIgnoreIfNull]
    public int? DailyMaxMmr { get; set; } = dailyMaxMmr;

    /// <summary>Games played that day, stored only when more than one.</summary>
    [BsonIgnoreIfNull]
    public int? Games { get; set; } = games;

    /// <summary>
    /// Rating deviation after the day's last game, kept only while the rating is
    /// still unconfident (see <see cref="PlayersObfuscator.RankDeviationObfuscationThreshold"/>).
    /// Once RD drops under that line it says nothing we act on, so it isn't
    /// stored and its absence means "settled".
    /// Never serialized: match responses already strip RD as internal, and the
    /// matchmaking service likewise exposes only a derived progress percentage.
    /// </summary>
    [JsonIgnore]
    [BsonIgnoreIfNull]
    public double? Rd { get; set; } = rd;

    /// <summary>
    /// Games that day, resolving the stored default. Absence means one game, which is
    /// what the omit-when-default rule encodes.
    ///
    /// On entries written before the field existed this reads as one game rather than
    /// as unknown. That is deliberately optimistic: the backfill rebuilds every day
    /// from the events, so the only entries it can be wrong about are ones a completed
    /// run never reached.
    /// </summary>
    public int GamesOrDefault => Games ?? 1;

    /// <summary>The day's peak, falling back to its closing rating.</summary>
    public int PeakMmr => DailyMaxMmr ?? Mmr;

    /// <summary>
    /// Whether the rating was still placing, and so shouldn't count towards a
    /// peak. Reuses the line the match API already draws for "not confident
    /// enough to show this MMR", rather than inventing a second one. Absence of
    /// Rd means the value was below it.
    /// </summary>
    public bool WasCalibrating =>
        Rd.HasValue && Rd.Value >= PlayersObfuscator.RankDeviationObfuscationThreshold;

    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset Date { get; set; } = date;

    /// <summary>
    /// Folds another entry for the same calendar day into this one.
    /// Order-independent: games accumulate, the peak is the highest close seen,
    /// and the latest entry defines the day's closing state.
    /// </summary>
    /// <remarks>
    /// Assumes each match is folded in exactly once - the games count accumulates
    /// and has no way to recognise a repeat. The caller is responsible for that;
    /// see <see cref="PlayerMmrRpTimeline.LastProcessedMatchId"/>.
    /// </remarks>
    public void MergeSameDay(MmrRpAtDate other)
    {
        var games = (Games ?? 1) + (other.Games ?? 1);
        var peak = Math.Max(PeakMmr, other.PeakMmr);

        if (other.Date >= Date)
        {
            Mmr = other.Mmr;
            Rp = other.Rp;
            Rd = other.Rd;
            Date = other.Date;
        }

        // Re-apply the omit-when-default rule, or a merged day would always
        // write both fields even when they carry nothing.
        Games = games > 1 ? games : null;
        DailyMaxMmr = peak > Mmr ? peak : null;
    }

    public Boolean HasSameYearMonthDayAs(MmrRpAtDate mRAT2)
    {
        return this.Date.Year == mRAT2.Date.Year &&
            this.Date.Month == mRAT2.Date.Month &&
            this.Date.Day == mRAT2.Date.Day;
    }

    public int CompareTo(object obj)
    {
        DateTimeOffset mmrTime_obj = ((MmrRpAtDate)obj).Date;
        if (this.Date < mmrTime_obj)
            return -1;
        if (this.Date > mmrTime_obj)
            return 1;
        return 0;
    }
}
