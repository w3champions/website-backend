using System;

namespace W3ChampionsStatisticService;

public static class MongoConnectionStringResolver
{
    /// <summary>
    /// Validates the MONGO_CONNECTION_STRING value. Fails fast when it is missing: silently falling back
    /// to a hard-coded host would point a misconfigured deployment at a shared database.
    /// </summary>
    public static string Resolve(string rawValue)
    {
        var connectionString = rawValue?.Replace("'", "").Trim();
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "MONGO_CONNECTION_STRING environment variable is not set or empty; refusing to start without an explicit MongoDB connection string.");
        }

        return connectionString;
    }
}
