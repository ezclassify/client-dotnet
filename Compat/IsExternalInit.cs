// Shared compatibility shim, linked into every project rather than copied.
// The net8.0 targets use the BCL type; only netstandard2.0 needs the shim.
#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    // C# 9 positional records require IsExternalInit, which ships in .NET 5+
    // but not in netstandard2.0.
    internal static class IsExternalInit { }
}
#endif
