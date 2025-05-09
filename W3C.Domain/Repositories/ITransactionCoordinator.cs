using MongoDB.Driver;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace W3C.Domain.Repositories;

public interface ITransactionCoordinator : IDisposable
{
    Guid InitializeTransaction();
    Guid CurrentTransactionId { get; }
    Task BeginTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default);
    Task AbortTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default);
    Task RegisterOnSuccessHandler(Func<Task> handler, bool executeImmediatelyWithoutTransaction = true);
    IClientSessionHandle GetCurrentSession();
    bool IsTransactionActive { get; }
}
