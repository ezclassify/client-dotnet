namespace EzClassify;

/// <summary>
/// Engine spec version baked in from <c>vendor/spec/MANIFEST.json</c> at
/// publish time.
///
/// <para>This equals the rest-api version this client is intended to
/// communicate with. A mismatch between this constant and
/// the wire version returned by <see cref="EzClassifyClient.PingAsync"/> indicates
/// a version skew that may cause subtle behavioral differences.</para>
/// </summary>
internal static class SpecVersion
{
    /// <summary>
    /// Engine spec version this client was built against.
    /// Stamped from the <c>vendor/spec/MANIFEST.json</c> version by the release
    /// automation. Do not edit by hand.
    /// </summary>
    public const string ClientSpecVersion = "1.0.0";
}
