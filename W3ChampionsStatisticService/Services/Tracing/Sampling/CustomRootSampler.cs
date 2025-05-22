using System;
using System.Diagnostics;
using OpenTelemetry.Trace;

namespace W3ChampionsStatisticService.Services.Tracing.Sampling;

public class CustomRootSampler : Sampler
{
    private readonly TraceIdRatioBasedSampler _serverRatioSampler;

    public CustomRootSampler(double serverSamplingRatio)
    {
        if (serverSamplingRatio < 0.0 || serverSamplingRatio > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(serverSamplingRatio), "Sampling ratio must be between 0.0 and 1.0");
        }
        // For root server spans, use TraceIdRatioBasedSampler
        _serverRatioSampler = new TraceIdRatioBasedSampler(serverSamplingRatio);
    }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        // This sampler is intended for use as the rootSampler within a ParentBasedSampler.
        // Thus, it's typically only called when there is no parent context.

        return samplingParameters.Kind switch
        {
            ActivityKind.Server => _serverRatioSampler.ShouldSample(samplingParameters), // Delegate to TraceIdRatioBasedSampler for SERVER spans when they are roots.
            _ => new SamplingResult(SamplingDecision.RecordAndSample), // Always sample for other kinds (Internal, Client, Producer, Consumer) when they are roots.
        };
    }
}
