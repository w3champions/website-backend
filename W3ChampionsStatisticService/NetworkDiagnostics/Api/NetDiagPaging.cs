namespace W3ChampionsStatisticService.NetworkDiagnostics.Api;

/// <summary>A5 paging rules shared by every paged C6 endpoint: zero-based page, page size 1-100.</summary>
public static class NetDiagPaging
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    /// <summary>Repositories skip page * pageSize documents; a larger page would overflow that product.</summary>
    public const int MaxPage = int.MaxValue / MaxPageSize;

    /// <summary>Null when valid, otherwise the 400 to answer.</summary>
    public static NetDiagResult Validate(int page, int pageSize)
    {
        if (page < 0) return NetDiagResult.Error(400, "page must be >= 0");
        if (pageSize is < 1 or > MaxPageSize) return NetDiagResult.Error(400, "pageSize must be in 1-100");
        if (page > MaxPage) return NetDiagResult.Error(400, "page is too large");
        return null;
    }
}
