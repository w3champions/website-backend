using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.RateLimiting.Models;

namespace W3ChampionsStatisticService.RateLimiting.Repositories;

public interface IApiTokenRepository
{
    // [NoTrace]: this is an interface proxy, so TracingInterceptor reads parameter attributes here; a raw API token
    // must never become a "param.token" activity tag.
    Task<ApiToken> GetByToken([NoTrace] string token);
    Task<ApiToken> GetById(string id);
    Task<List<ApiToken>> GetAll();
    Task Create(ApiToken apiToken);
    Task Update(ApiToken apiToken);
    Task Delete(string id);
    Task UpdateLastUsed([NoTrace] string token);
}

public class ApiTokenRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IApiTokenRepository
{
    [Trace]
    public async Task<ApiToken> GetByToken(string token)
    {
        return await LoadFirst<ApiToken>(t => t.Token == token && t.IsActive);
    }

    [Trace]
    public async Task<ApiToken> GetById(string id)
    {
        return await LoadFirst<ApiToken>(t => t.Id == id);
    }

    [Trace]
    public async Task<List<ApiToken>> GetAll()
    {
        return await LoadAll<ApiToken>();
    }

    [Trace]
    public async Task Create(ApiToken apiToken)
    {
        await Insert(apiToken);
    }

    [Trace]
    public async Task Update(ApiToken apiToken)
    {
        await Upsert(apiToken);
    }

    [Trace]
    public async Task Delete(string id)
    {
        var collection = CreateCollection<ApiToken>();
        await collection.DeleteOneAsync(t => t.Id == id);
    }

    public async Task UpdateLastUsed(string token)
    {
        var collection = CreateCollection<ApiToken>();
        var update = Builders<ApiToken>.Update.Set(t => t.LastUsedAt, DateTimeOffset.UtcNow);
        await collection.UpdateOneAsync(t => t.Token == token, update);
    }
}
