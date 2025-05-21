using System;
using System.Runtime.CompilerServices;

namespace W3C.Domain.Tracing;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TraceAttribute([CallerMemberName] string operationName = null) : Attribute
{
    public string OperationName { get; } = operationName;

    // If you need an explicit override and want to keep the primary constructor for [CallerMemberName]
    // you might need a static factory method or a different named attribute if C# rules get too complex.
    // For now, this primary constructor with [CallerMemberName] is the simplest for auto-naming.
} 