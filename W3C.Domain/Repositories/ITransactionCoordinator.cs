using MongoDB.Driver;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace W3C.Domain.Repositories;

public interface ITransactionCoordinator : IDisposable
{
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task AbortTransactionAsync(CancellationToken cancellationToken = default);
    Task RegisterOnSuccessHandler(Func<Task> handler, bool executeImmediatelyWithoutTransaction = true);
    IClientSessionHandle GetCurrentSession();
    bool IsTransactionActive { get; }
}
