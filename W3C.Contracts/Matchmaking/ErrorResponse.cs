namespace W3C.Contracts.Matchmaking;

public class ErrorResponse
{
    public MMError[] Errors { get; set; } = new MMError[0];

    // Some matchmaking-service endpoints (e.g. commercial-license) answer { "error": "<message>" } instead of { "errors": [...] }.
    public string Error { get; set; }
}
