namespace W3C.Contracts.Matchmaking;

public class GetMapsRequest
{
    public string Filter { get; set; }
    public int Offset { get; set; } = 0;
    public int Limit { get; set; } = 10;

    /// <summary>
    /// Admin-only: include temporary (self-provided) maps in the listing. matchmaking honours it only
    /// for admin-secret callers and strips mapProof/proofHash from every row it returns.
    /// </summary>
    public bool IncludeTemporary { get; set; }
}
