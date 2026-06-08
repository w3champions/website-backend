using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// Permanent, reset-immune per-player peak-rank store. One document per battleTag (Id = battleTag),
// holding the highest rank ever reached per (game mode, race), plus per-season peaks and a reserved
// badge slot. Survives the seasonal reset because the _id is stable and rows are only ever upserted.
public class ProgressionPrestige : IIdentifiable
{
    public string Id { get; set; }
    public List<PrestigePeakEntry> Peaks { get; set; } = new();

    public static ProgressionPrestige Create(string battleTag) => new() { Id = battleTag };

    public void RecordPeak(GameMode gameMode, Race? race, PeakRank candidate)
    {
        if (candidate?.League == null) return; // ignore unplaced/calibrating ranks; the store holds placed ranks only

        var entry = Peaks.FirstOrDefault(e => e.GameMode == gameMode && e.Race == race);
        if (entry == null)
        {
            entry = new PrestigePeakEntry { GameMode = gameMode, Race = race };
            Peaks.Add(entry);
        }

        entry.Record(candidate);
    }
}

// Peak track for one (game mode, race). Race is set only for race-split modes (1v1); null otherwise.
public class PrestigePeakEntry
{
    public GameMode GameMode { get; set; }
    public Race? Race { get; set; }
    public PeakRank AllTimePeak { get; set; }
    public List<PeakRank> SeasonPeaks { get; set; } = new();

    // Reserved cosmetic slot. Persisted and retained; no awarding logic in this stage.
    public List<PrestigeBadge> Badges { get; set; } = new();

    // Internal so callers must route through ProgressionPrestige.RecordPeak, which owns the
    // (game mode, race) entry lookup; tests reach it via InternalsVisibleTo.
    internal void Record(PeakRank candidate)
    {
        var index = SeasonPeaks.FindIndex(p => p.Season == candidate.Season);
        if (index < 0)
        {
            SeasonPeaks.Add(candidate);
        }
        else if (PrestigeRankComparer.IsHigher(candidate, SeasonPeaks[index]))
        {
            SeasonPeaks[index] = candidate;
        }

        if (AllTimePeak == null || PrestigeRankComparer.IsHigher(candidate, AllTimePeak))
        {
            AllTimePeak = candidate;
        }
    }
}

// Reserved placeholder — never populated in this stage; exists so the slot round-trips and survives resets.
// BSON-safe: implicit parameterless ctor + settable props; keep it that way if fields are added.
public class PrestigeBadge
{
    public string Code { get; set; }
    public int Season { get; set; }
}
