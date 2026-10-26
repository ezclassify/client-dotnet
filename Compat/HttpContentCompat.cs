// Shared compatibility shim, linked into the project that makes HTTP calls
// rather than copied into it. Declared in the EzClassify namespace so
// code in EzClassify sees the extension method without an extra using.
#if NETSTANDARD2_0
namespace EzClassify
{
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Backfills the <c>ReadAsStringAsync(CancellationToken)</c> overload added
    /// in .NET 5. netstandard2.0's HttpContent cannot cancel an in-flight body
    /// read, so this checks the token up front and delegates to the
    /// parameterless overload.
    /// </summary>
    internal static class HttpContentCompat
    {
        public static Task<string> ReadAsStringAsync(
            this HttpContent content,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return content.ReadAsStringAsync();
        }
    }
}
#endif
