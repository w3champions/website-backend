namespace W3C.Domain;

/// <summary>
/// Names of the outbound <see cref="System.Net.Http.HttpClient"/>s. Each one is registered with an
/// explicit timeout (W3ChampionsStatisticService/Extensions/HttpClientServiceCollectionExtensions.cs);
/// resolve them via <c>IHttpClientFactory.CreateClient(name)</c>, never with <c>new HttpClient()</c>.
/// </summary>
public static class HttpClientNames
{
    public const string Matchmaking = "matchmaking";
    /// <summary>Matchmaking admin log listing/download: larger payloads than the regular API.</summary>
    public const string MatchmakingLogs = "matchmaking-logs";
    public const string Identification = "identification";
    public const string Chat = "chat";
    public const string Update = "update";
    public const string Replay = "replay";
    public const string War3Info = "war3info";
}
