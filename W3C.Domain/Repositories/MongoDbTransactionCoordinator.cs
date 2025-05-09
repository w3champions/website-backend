using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;

namespace W3C.Domain.Repositories;

public class MongoDbTransactionCoordinator : ITransactionCoordinator
{
    private readonly IMongoClient _mongoClient;
    private IClientSessionHandle _session;
    private ConcurrentQueue<Func<Task>> _onSuccessHandlers;

    public bool IsTransactionActive => _session != null && _session.IsInTransaction;

    public MongoDbTransactionCoordinator(IMongoClient mongoClient)
    {
        _mongoClient = mongoClient ?? throw new ArgumentNullException(nameof(mongoClient));
        _onSuccessHandlers = new ConcurrentQueue<Func<Task>>();
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (IsTransactionActive)
        {
            throw new InvalidOperationException("A transaction is already in progress.");
        }
        _session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        _session.StartTransaction();
        ClearQueue();
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsTransactionActive)
        {
            throw new InvalidOperationException("No active transaction to commit.");
        }
        await _session.CommitTransactionAsync(cancellationToken);
        // Transaction was successfully committed, execute all onSuccess handlers
        await ExecuteOnSuccessHandlersAsync();
    }

    public async Task AbortTransactionAsync(CancellationToken cancellationToken = default)
    {
        ClearQueue();

        if (!IsTransactionActive)
        {
            // It's often safe to allow aborting a non-existent or already completed transaction.
            // However, if strictness is desired, an exception can be thrown.
            // For now, let's allow it to be called safely.
            return;
        }
        await _session.AbortTransactionAsync(cancellationToken);
    }

    public async Task ExecuteOnSuccessHandlersAsync()
    {
        while (_onSuccessHandlers.TryDequeue(out var handler))
        {
            await handler();
        }
    }

    private void ClearQueue()
    {
        while (_onSuccessHandlers.TryDequeue(out _)) { }
    }

    public Task RegisterOnSuccessHandler(Func<Task> handler, bool executeImmediatelyWithoutTransaction = true)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        if (IsTransactionActive)
        {
            _onSuccessHandlers.Enqueue(handler);
            return Task.CompletedTask;
        }

        if (executeImmediatelyWithoutTransaction)
        {
            return handler();
        }

        throw new InvalidOperationException("No transaction is active, and immediate execution without a transaction is not allowed for this handler.");
    }

    public IClientSessionHandle GetCurrentSession()
    {
        return _session;
    }

    public void Dispose()
    {
        ClearQueue();
        _session?.Dispose();
        _session = null;
    }
}
