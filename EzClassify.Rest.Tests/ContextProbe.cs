namespace EzClassify.Tests;

/// <summary>
/// The smallest Context description the contract accepts, for stub transports
/// that answer the binding probe <c>ConnectAsync</c> sends and have nothing
/// else to say about the Context.
/// </summary>
internal static class ContextProbe
{
    public const string Body =
        """{"id":"stub","version":"1.0.0","scheme":{"id":"stub-scheme","version":"1.0.0","resolvedVersion":"1.0.0"},"schemes":[]}""";
}
