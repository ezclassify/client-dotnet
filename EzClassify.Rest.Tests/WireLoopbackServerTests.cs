using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Xunit;

namespace EzClassify.Tests;

// These tests reclaim the exact released port; parallel loopback fixtures may claim it first.
[CollectionDefinition(nameof(WireLoopbackServerCollection), DisableParallelization = true)]
public sealed class WireLoopbackServerCollection { }

[Collection(nameof(WireLoopbackServerCollection))]
public class WireLoopbackServerTests
{
    [Fact]
    public async Task DisposalDoesNotRebindAPortClaimedWhileAwaitingTheServerLoop()
    {
        using var handler = new WireReplayHandler(WireCorpus.Vendored, new[] { "health.ok" }, null);
        var server = WireLoopbackServer.Start(handler);
        var listener = Listener(server);
        var loopField = typeof(WireLoopbackServer).GetField("_loop", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originalLoop = Assert.IsAssignableFrom<Task>(loopField.GetValue(server));
        var finishLoop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Hold the existing shutdown await so another real socket can claim the released port.
        loopField.SetValue(server, finishLoop.Task);
        var disposal = server.DisposeAsync().AsTask();
        using var replacement = new TcpListener(IPAddress.Loopback, new Uri(server.Url).Port);
        try
        {
            Assert.False(listener.IsListening);
            Assert.False(disposal.IsCompleted);
            replacement.Start();
            finishLoop.SetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            using var client = new TcpClient();
            var accepted = replacement.AcceptTcpClientAsync();
            await client.ConnectAsync(IPAddress.Loopback, new Uri(server.Url).Port);
            using var connection = await accepted.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(connection.Connected);
        }
        finally
        {
            finishLoop.TrySetResult();
            await originalLoop.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task DisposalStillReportsUnexpectedServerFailure()
    {
        using var handler = new WireReplayHandler(WireCorpus.Vendored, new[] { "health.ok" }, null);
        var server = WireLoopbackServer.Start(handler);
        var listener = Listener(server);
        var loopField = typeof(WireLoopbackServer).GetField("_loop", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originalLoop = Assert.IsAssignableFrom<Task>(loopField.GetValue(server));
        var failure = new IOException("controlled server failure");
        loopField.SetValue(server, Task.FromException(failure));
        var observed = await Assert.ThrowsAsync<IOException>(() => server.DisposeAsync().AsTask());
        Assert.Same(failure, observed);
        Assert.False(listener.IsListening);
        await originalLoop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IdleServerDisposalReleasesItsRealPort()
    {
        using var handler = new WireReplayHandler(WireCorpus.Vendored, new[] { "health.ok" }, null);
        var server = WireLoopbackServer.Start(handler);
        var listener = Listener(server);
        await server.DisposeAsync();
        Assert.False(listener.IsListening);
        using var replacement = new TcpListener(IPAddress.Loopback, new Uri(server.Url).Port);
        replacement.Start();
        Assert.True(replacement.Server.IsBound);
    }

    private static HttpListener Listener(WireLoopbackServer server) =>
        Assert.IsType<HttpListener>(typeof(WireLoopbackServer)
            .GetField("_listener", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server));
}
