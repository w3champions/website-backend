using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace WC3ChampionsStatisticService.Tests;

/// <summary>
/// A Serilog sink that keeps every event written to it, for fixtures that assert what was (or was never) logged.
/// Attach it to a logger configuration with <c>WriteTo.Sink(sink)</c>, build a Verbose logger over it with
/// <see cref="CreateLogger"/>, or route the static <see cref="Log.Logger"/> to it with <see cref="CaptureStaticLogger"/>.
/// </summary>
public sealed class CapturingLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    /// <summary>A snapshot of the events emitted so far, in emission order.</summary>
    public IReadOnlyList<LogEvent> Events => [.. _events];

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    /// <summary>A logger that writes every level, Verbose included, to this sink only.</summary>
    public Logger CreateLogger() => new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();

    /// <summary>
    /// Routes the static <see cref="Log.Logger"/> to this sink until the returned scope is disposed, then restores the
    /// previous logger. Code that logs through <see cref="Log"/> directly is only observable this way.
    /// </summary>
    public IDisposable CaptureStaticLogger() => new StaticLoggerScope(CreateLogger());

    private sealed class StaticLoggerScope : IDisposable
    {
        private readonly ILogger _previous = Log.Logger;
        private readonly Logger _capturing;

        public StaticLoggerScope(Logger capturing)
        {
            _capturing = capturing;
            Log.Logger = capturing;
        }

        public void Dispose()
        {
            Log.Logger = _previous;
            _capturing.Dispose();
        }
    }
}
