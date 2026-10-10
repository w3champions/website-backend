using System;
using MongoDB.Driver;

namespace W3ChampionsStatisticService;

public static class MongoClientSettingsFactory
{
    public static readonly TimeSpan ServerSelectionTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long an operation may wait for a pooled connection. The driver default is 2 minutes: when
    /// the pool cannot keep up, requests then queue for minutes instead of failing, which keeps the
    /// backlog (and the threads behind it) growing. Fail fast and let the caller retry.
    /// </summary>
    public static readonly TimeSpan WaitQueueTimeout = TimeSpan.FromSeconds(10);

    /// <param name="connectionString">An already validated value, see <see cref="MongoConnectionStringResolver"/>.</param>
    public static MongoClientSettings Create(string connectionString)
    {
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        settings.ServerSelectionTimeout = ServerSelectionTimeout;
        settings.ConnectTimeout = ConnectTimeout;
        settings.WaitQueueTimeout = WaitQueueTimeout;
        return settings;
    }
}
