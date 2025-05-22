using System;
using System.Runtime.CompilerServices;

namespace W3C.Domain.Tracing;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TraceAttribute([CallerMemberName] string operationName = null) : Attribute
{
    public string OperationName { get; } = operationName;
}
