using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.Admin.ReplayExport;

public enum ReplayExportOrder
{
    Newest = 0,
    Oldest = 1,
    Random = 2,
}

public enum ReplayExportMmrMode
{
    /// <summary>Every player in the game is inside the MMR range.</summary>
    EveryPlayer = 0,

    /// <summary>At least one player in the game is inside the MMR range.</summary>
    AnyPlayer = 1,
}

public class ReplayExportFilter
{
    public GameMode GameMode { get; set; } = GameMode.GM_1v1;

    /// <summary>Patch version from the Patch collection. Null or empty means any patch.</summary>
    public string Patch { get; set; }

    /// <summary>Ladder seasons. Empty means any season.</summary>
    public List<int> Seasons { get; set; } = [];

    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public GateWay? GateWay { get; set; }
    public string MapName { get; set; }
    public string BattleTag { get; set; }
    public int? MinMmr { get; set; }
    public int? MaxMmr { get; set; }
    public ReplayExportMmrMode MmrMode { get; set; } = ReplayExportMmrMode.EveryPlayer;

    /// <summary>In 1v1, the race of one side. In other modes, a race that at least one player played.</summary>
    public Race? Race { get; set; }

    /// <summary>1v1 only: the race of the other side.</summary>
    public Race? OpponentRace { get; set; }

    public int? MinDurationSeconds { get; set; }
    public int? MaxDurationSeconds { get; set; }

    /// <summary>
    /// At most this many games in the list ("Max games" on the page). Without it, the list has all games found,
    /// up to the limit of one download. When more games match, <see cref="Order"/> selects which ones.
    /// </summary>
    public int Quantity { get; set; } = ReplayExportRepository.MaxQuantity;

    public ReplayExportOrder Order { get; set; } = ReplayExportOrder.Newest;

    /// <summary>Leaves out every game whose replay was in an earlier download (see <see cref="ReplayExportRecord"/>).</summary>
    public bool ExcludePastDownloads { get; set; }

    /// <summary>With <see cref="ExcludePastDownloads"/>: only these downloads. Empty means all of them.</summary>
    public List<string> ExcludeExportIds { get; set; } = [];

    /// <summary>A copy for the manifest, without <see cref="ExcludeExportIds"/>: the ids of downloads are internal.</summary>
    public ReplayExportFilter ForManifest()
    {
        var copy = (ReplayExportFilter)MemberwiseClone();
        copy.ExcludeExportIds = [];
        return copy;
    }
}

public class ReplayExportPatchWindow
{
    public DateTime From { get; set; }

    /// <summary>Null for the patch that is live now.</summary>
    public DateTime? To { get; set; }
}

public class ReplayExportPatchOption
{
    public string Version { get; set; }
    public List<ReplayExportPatchWindow> Windows { get; set; } = [];
}

public class ReplayExportOptions
{
    public List<ReplayExportPatchOption> Patches { get; set; } = [];

    /// <summary>Season ids, newest first.</summary>
    public List<int> Seasons { get; set; } = [];
    public List<string> MapNames { get; set; } = [];
    public int MaxQuantity { get; set; }
    public ReplayExportPrices Prices { get; set; } = new();
}

public class ReplayExportPlayer
{
    public string BattleTag { get; set; }
    public Race Race { get; set; }
    public Race? RndRace { get; set; }
    public int? OldMmr { get; set; }
    public bool Won { get; set; }
}

public class ReplayExportTeam
{
    public List<ReplayExportPlayer> Players { get; set; } = [];
}

public class ReplayExportMatch
{
    public string Id { get; set; }
    public string MatchId { get; set; }
    public int FloMatchId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    /// <summary>
    /// When the game ended. The shown patch uses it. The order and the filters use the time in the id, which
    /// is set when the game ends.
    /// </summary>
    public DateTimeOffset EndTime { get; set; }
    public long DurationInSeconds { get; set; }
    public GameMode GameMode { get; set; }
    public GateWay GateWay { get; set; }
    public int Season { get; set; }
    public string Map { get; set; }
    public string MapName { get; set; }
    public string Patch { get; set; }
    public int? AverageMmr { get; set; }
    public List<ReplayExportTeam> Teams { get; set; } = [];
}

public class ReplayExportPreview
{
    /// <summary>
    /// Number of games that match the filter, counted up to <see cref="ReplayExportRepository.CountLimit"/>.
    /// <see cref="TotalIsCapped"/> is true when the count stopped there or at its time limit.
    /// </summary>
    public long TotalMatching { get; set; }
    public bool TotalIsCapped { get; set; }

    /// <summary>
    /// The ids of all listed games, in list order. Only ids: the page selects and downloads by id, and gets
    /// the games of each table page with <c>POST matches</c>.
    /// </summary>
    public List<string> MatchIds { get; set; } = [];

    /// <summary>The games of the first table page, so the page needs no second request.</summary>
    public List<ReplayExportMatch> Matches { get; set; } = [];
}

/// <summary>The games of one table page.</summary>
public class ReplayExportMatchesRequest
{
    public List<string> MatchIds { get; set; } = [];
}

public class ReplayExportTicketRequest
{
    public List<string> MatchIds { get; set; } = [];

    /// <summary>Optional name for the export history, for example the buyer or the order.</summary>
    public string Label { get; set; }

    /// <summary>One line about the selection, for the export history. The page writes it.</summary>
    public string Description { get; set; }

    /// <summary>The filter that selected the games, for the manifest and the history.</summary>
    public ReplayExportFilter Filter { get; set; }
}

public class ReplayExportTicketResponse
{
    public string Ticket { get; set; }
    public int Count { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The list price of the download, and whether the user pays it (admins do not).</summary>
    public decimal Price { get; set; }
    public bool Charged { get; set; }
}

/// <summary>
/// One finished download, in the ReplayExports collection. Later searches can leave out its games, so a buyer
/// does not get the same replay twice. The ZIP itself is not kept: a repeat download builds it again from
/// <see cref="MatchIds"/>.
/// </summary>
public class ReplayExportRecord
{
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>When the first download finished.</summary>
    public DateTime CreatedAt { get; set; }
    public string RequestedBy { get; set; }
    public string Label { get; set; }
    public string Description { get; set; }
    public int DownloadCount { get; set; } = 1;
    public DateTime LastDownloadedAt { get; set; }

    /// <summary>Who downloaded the ZIP and when: the first download, then each repeat download.</summary>
    public List<ReplayExportDownload> Downloads { get; set; } = [];

    /// <summary>Selected games that the server found with a FLO replay. Some of them can fail (see <see cref="Failed"/>).</summary>
    public int Requested { get; set; }

    /// <summary>Games whose replay is in the ZIP. Only these count as exported.</summary>
    public List<ObjectId> MatchIds { get; set; } = [];
    public int Failed { get; set; }
    public ReplayExportFilter Filter { get; set; }

    /// <summary>The list price of the replays in the ZIP, in <see cref="ReplayExportPricing.Currency"/>.</summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Price { get; set; }

    /// <summary>False when the user did not pay (admins).</summary>
    public bool Charged { get; set; }
}

public class ReplayExportDownload
{
    public string By { get; set; }
    public DateTime At { get; set; }
}

public class ReplayExportSummary
{
    public string Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string RequestedBy { get; set; }
    public string Label { get; set; }
    public string Description { get; set; }
    public int Requested { get; set; }

    /// <summary>Replays in the ZIP.</summary>
    public int Count { get; set; }
    public int Failed { get; set; }
    public int DownloadCount { get; set; }
    public DateTime LastDownloadedAt { get; set; }
    public List<ReplayExportDownload> Downloads { get; set; } = [];
    public decimal Price { get; set; }
    public string Currency { get; set; } = ReplayExportPricing.Currency;
    public bool Charged { get; set; }
}
