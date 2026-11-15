using System.Linq;
using Serilog.Core;
using Serilog.Events;

namespace W3ChampionsStatisticService.Services.Tracing;

/// <summary>
/// Redacts machine IDs in string log properties, in particular the RequestPath scope ASP.NET Core attaches to every
/// framework log written during a request. Registered on the root logger, so it runs after the scope is added.
/// </summary>
public sealed class MachineIdLogEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var affected = logEvent.Properties
            .Where(p => p.Value is ScalarValue { Value: string text } && !ReferenceEquals(MachineIdRedaction.Redact(text), text))
            .Select(p => (p.Key, Text: (string)((ScalarValue)p.Value).Value))
            .ToList();
        foreach (var (key, text) in affected)
        {
            logEvent.AddOrUpdateProperty(new LogEventProperty(key, new ScalarValue(MachineIdRedaction.Redact(text))));
        }
    }
}
