using System;
using System.Threading.Tasks;

namespace W3C.Domain.Repositories;

public sealed class AsyncTransactionScope : IAsyncDisposable
{
    private readonly ITransactionCoordinator _transactionCoordinator;
    private bool _completedSuccessfully = false;

    private AsyncTransactionScope(ITransactionCoordinator transactionCoordinator)
    {
        _transactionCoordinator = transactionCoordinator;
    }

    public static async Task<AsyncTransactionScope> CreateAsync(ITransactionCoordinator transactionCoordinator)
    {
        if (transactionCoordinator == null)
        {
            throw new ArgumentNullException(nameof(transactionCoordinator));
        }
        await transactionCoordinator.BeginTransactionAsync();
        return new AsyncTransactionScope(transactionCoordinator);
    }

    public void Complete()
    {
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
            await _transactionCoordinator.CommitTransactionAsync();
        }
        else
        {
            await _transactionCoordinator.AbortTransactionAsync();
        }
    }

    public async Task RegisterOnSuccessHandler(Func<Task> handler, bool executeImmediatelyWithoutTransaction = true)
    {
        await _transactionCoordinator.RegisterOnSuccessHandler(handler, executeImmediatelyWithoutTransaction);
    }
}
