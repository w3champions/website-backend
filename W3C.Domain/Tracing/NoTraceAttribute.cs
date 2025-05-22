using System;

namespace W3C.Domain.Tracing;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter, Inherited = true, AllowMultiple = false)]
public sealed class NoTraceAttribute : Attribute
{
    public NoTraceAttribute() { }
}
