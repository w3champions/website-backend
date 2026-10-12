using System;

namespace W3ChampionsStatisticService.NetworkDiagnostics;

/// <summary>Injected randomness so audience and slot decisions are unit-testable (spec §5.2).</summary>
public interface IRandom
{
    double NextDouble();
    int Next(int maxExclusive);
}

public sealed class SystemRandom : IRandom
{
    public double NextDouble() => Random.Shared.NextDouble();
    public int Next(int maxExclusive) => Random.Shared.Next(maxExclusive);
}
