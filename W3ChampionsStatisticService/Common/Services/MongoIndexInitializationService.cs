using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Common.Services;

/// <summary>
/// Background service that ensures MongoDB indexes are created at application startup.
/// This service runs once when the application starts and creates all required indexes
/// for repositories that implement IRequiresIndexes.
/// </summary>
public class MongoIndexInitializationService(
    IServiceProvider serviceProvider,
    ILogger<MongoIndexInitializationService> logger) : IHostedService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<MongoIndexInitializationService> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting MongoDB index initialization...");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var scope = _serviceProvider.CreateScope();
            
            // Get all repositories that require indexes
            var repositoriesWithIndexes = scope.ServiceProvider
                .GetServices<IRequiresIndexes>()
                .ToList();

            if (!repositoriesWithIndexes.Any())
            {
                _logger.LogWarning("No repositories implementing IRequiresIndexes found");
                return;
            }

            _logger.LogInformation("Found {Count} repositories requiring index initialization", 
                repositoriesWithIndexes.Count);

            var tasks = new List<Task>();
            
            foreach (var repository in repositoriesWithIndexes)
            {
                // Create indexes in parallel for better performance
                tasks.Add(CreateIndexesForRepository(repository, cancellationToken));
            }

            await Task.WhenAll(tasks);

            stopwatch.Stop();
            _logger.LogInformation("MongoDB index initialization completed in {ElapsedMs}ms", 
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error during MongoDB index initialization after {ElapsedMs}ms", 
                stopwatch.ElapsedMilliseconds);
            
            // Don't throw - we don't want to prevent the application from starting
            // if index creation fails. The indexes can be created manually if needed.
        }
    }

    private async Task CreateIndexesForRepository(IRequiresIndexes repository, CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Index creation for repository {RepositoryName} cancelled", repository.CollectionName);
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Creating indexes for collection: {CollectionName}", 
                repository.CollectionName);
            
            await repository.EnsureIndexesAsync();
            
            stopwatch.Stop();
            _logger.LogInformation("Successfully created indexes for collection: {CollectionName} in {ElapsedMs}ms", 
                repository.CollectionName, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create indexes for collection: {CollectionName}", 
                repository.CollectionName);
            // Don't throw - continue with other repositories
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to clean up
        return Task.CompletedTask;
    }
}