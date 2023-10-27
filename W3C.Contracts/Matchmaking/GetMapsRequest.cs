namespace W3C.Contracts.Matchmaking;

public class GetMapsRequest
{
    public string Filter { get; set; }
    public int Offset { get; set; } = 0;
    public int Limit { get; set; } = 10;
}
