using System;
using System.Threading.Tasks;

namespace W3C.Domain.Repositories;

public sealed class AsyncTransactionScope : IAsyncDisposable
{
    private readonly ITransactionCoordinator _transactionCoordinator;
    private readonly Guid _transactionId;
    private bool _completedSuccessfully = false;

    private AsyncTransactionScope(ITransactionCoordinator transactionCoordinator, Guid transactionId)
    {
        _transactionCoordinator = transactionCoordinator;
        _transactionId = transactionId;
    }

    public static AsyncTransactionScope Create(ITransactionCoordinator transactionCoordinator)
    {
        if (transactionCoordinator == null)
        {
            throw new ArgumentNullException(nameof(transactionCoordinator));
        }
        var transactionId = transactionCoordinator.InitializeTransaction();
        return new AsyncTransactionScope(transactionCoordinator, transactionId);
    }

    public async Task Start()
    {
        await _transactionCoordinator.BeginTransactionAsync(_transactionId);
    }

    public void Complete()
    {
        if (_transactionId != _transactionCoordinator.CurrentTransactionId)
        {
            throw new InvalidOperationException("Transaction id mismatch: " + _transactionId + " != " + _transactionCoordinator.CurrentTransactionId);
        }
        if (!_transactionCoordinator.IsTransactionActive)
        {
            throw new InvalidOperationException("Cannot complete a transaction that is not active.");
        }
        _completedSuccessfully = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_transactionCoordinator.IsTransactionActive)
        {
            return;
        }

        if (_completedSuccessfully)
        {
            await _transactionCoordinator.CommitTransactionAsync(_transactionId);
        }
        else
        {
            await _transactionCoordinator.AbortTransactionAsync(_transactionId);
        }
    }

    public async Task RegisterOnSuccessHandler(Func<Task> handler, bool executeImmediatelyWithoutTransaction = true)
    {
        await _transactionCoordinator.RegisterOnSuccessHandler(handler, executeImmediatelyWithoutTransaction);
    }
}
