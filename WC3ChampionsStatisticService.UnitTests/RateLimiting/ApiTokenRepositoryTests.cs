using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.RateLimiting.Models;
using W3ChampionsStatisticService.RateLimiting.Repositories;

namespace WC3ChampionsStatisticService.Tests.RateLimiting;

[TestFixture]
public class ApiTokenRepositoryTests : IntegrationTestBase
{
    private ApiTokenRepository _repository;

    [SetUp]
    public new async Task Setup()
    {
        await base.Setup();
        _repository = new ApiTokenRepository(MongoClient);
    }

    [Test]
    public async Task Create_ShouldInsertToken()
    {
        var token = new ApiToken
        {
            Name = "Test Token",
            Description = "Test Description",
            ContactDetails = "test@example.com",
            Token = "test-token-123",
            IsActive = true
        };

        await _repository.Create(token);

        var retrieved = await _repository.GetById(token.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Name, Is.EqualTo("Test Token"));
        Assert.That(retrieved.Token, Is.EqualTo("test-token-123"));
    }

    [Test]
    public async Task GetByToken_WhenActive_ShouldReturnToken()
    {
        var token = new ApiToken
        {
            Name = "Active Token",
            Token = "active-token-123",
            IsActive = true
        };
        await _repository.Create(token);

        var result = await _repository.GetByToken("active-token-123");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("Active Token"));
    }

    [Test]
    public async Task GetByToken_WhenInactive_ShouldReturnNull()
    {
        var token = new ApiToken
        {
            Name = "Inactive Token",
            Token = "inactive-token-123",
            IsActive = false
        };
        await _repository.Create(token);

        var result = await _repository.GetByToken("inactive-token-123");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetByToken_WhenNotExists_ShouldReturnNull()
    {
        var result = await _repository.GetByToken("non-existent-token");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task Update_ShouldModifyExistingToken()
    {
        var token = new ApiToken
        {
            Name = "Original Name",
            Description = "Original Description",
            IsActive = true
        };
        await _repository.Create(token);

        token.Name = "Updated Name";
        token.Description = "Updated Description";
        token.IsActive = false;
        await _repository.Update(token);

        var retrieved = await _repository.GetById(token.Id);
        Assert.That(retrieved.Name, Is.EqualTo("Updated Name"));
        Assert.That(retrieved.Description, Is.EqualTo("Updated Description"));
        Assert.That(retrieved.IsActive, Is.False);
    }

    [Test]
    public async Task Delete_ShouldRemoveToken()
    {
        var token = new ApiToken
        {
            Name = "Token to Delete"
        };
        await _repository.Create(token);

        await _repository.Delete(token.Id);

        var retrieved = await _repository.GetById(token.Id);
        Assert.That(retrieved, Is.Null);
    }

    [Test]
    public async Task GetAll_ShouldReturnAllTokens()
    {
        await _repository.Create(new ApiToken { Name = "Token 1" });
        await _repository.Create(new ApiToken { Name = "Token 2" });
        await _repository.Create(new ApiToken { Name = "Token 3" });

        var tokens = await _repository.GetAll();

        Assert.That(tokens.Count, Is.EqualTo(3));
        Assert.That(tokens.Exists(t => t.Name == "Token 1"), Is.True);
        Assert.That(tokens.Exists(t => t.Name == "Token 2"), Is.True);
        Assert.That(tokens.Exists(t => t.Name == "Token 3"), Is.True);
    }

    [Test]
    public async Task UpdateLastUsed_ShouldUpdateTimestamp()
    {
        var token = new ApiToken
        {
            Name = "Token to Update",
            Token = "update-test-token",
            LastUsedAt = null
        };
        await _repository.Create(token);

        await _repository.UpdateLastUsed("update-test-token");

        var retrieved = await _repository.GetById(token.Id);
        Assert.That(retrieved.LastUsedAt, Is.Not.Null);
        Assert.That(retrieved.LastUsedAt.Value, Is.GreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    [Test]
    public async Task Create_WithScopes_ShouldPersistScopes()
    {
        var token = new ApiToken
        {
            Name = "Token with Scopes",
            Scopes = new Dictionary<string, ApiTokenScope>
            {
                ["replay"] = new ApiTokenScope { HourlyLimit = 100, DailyLimit = 1000, IsEnabled = true },
                ["stats"] = new ApiTokenScope { HourlyLimit = 50, DailyLimit = 500, IsEnabled = false }
            }
        };
        await _repository.Create(token);

        var retrieved = await _repository.GetById(token.Id);

        Assert.That(retrieved.Scopes.Count, Is.EqualTo(2));
        Assert.That(retrieved.Scopes["replay"].HourlyLimit, Is.EqualTo(100));
        Assert.That(retrieved.Scopes["replay"].DailyLimit, Is.EqualTo(1000));
        Assert.That(retrieved.Scopes["replay"].IsEnabled, Is.True);
        Assert.That(retrieved.Scopes["stats"].IsEnabled, Is.False);
    }

    [Test]
    public async Task Create_WithAllowedIPs_ShouldPersistIPs()
    {
        var token = new ApiToken
        {
            Name = "Token with IP Restrictions",
            AllowedIPs = new[] { "192.168.1.1", "10.0.0.1" }
        };
        await _repository.Create(token);

        var retrieved = await _repository.GetById(token.Id);

        Assert.That(retrieved.AllowedIPs.Length, Is.EqualTo(2));
        Assert.That(retrieved.AllowedIPs, Contains.Item("192.168.1.1"));
        Assert.That(retrieved.AllowedIPs, Contains.Item("10.0.0.1"));
    }

    [Test]
    public async Task Create_WithExpiryDate_ShouldPersistExpiry()
    {
        var expiryDate = DateTimeOffset.UtcNow.AddDays(30);
        var token = new ApiToken
        {
            Name = "Token with Expiry",
            ExpiresAt = expiryDate
        };
        await _repository.Create(token);

        var retrieved = await _repository.GetById(token.Id);

        Assert.That(retrieved.ExpiresAt, Is.Not.Null);
        Assert.That(retrieved.ExpiresAt.Value, Is.EqualTo(expiryDate).Within(TimeSpan.FromSeconds(1)));
    }
}