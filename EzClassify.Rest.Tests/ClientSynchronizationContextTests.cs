using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

// ---------------------------------------------------------------------------
// Sync-over-async deadlock regression.
//
// The client library targets netstandard2.0, so it runs on hosts with a
// blocking SynchronizationContext (WinForms, WPF, classic ASP.NET). A consumer
// there may call `client.ProcessAsync(x).Result`. Every await inside the
// library must use ConfigureAwait(false); otherwise its continuation posts
// back to the captured context, which the blocked caller thread never pumps,
// and the call deadlocks forever. These tests run each transport path under a
// context that never executes posted continuations: a missing
// ConfigureAwait(false) turns into a hang that the watchdog join reports.
// ---------------------------------------------------------------------------

/// <summary>
/// A SynchronizationContext that swallows every posted continuation, modeling
/// a UI thread that is blocked in sync-over-async and can never pump. Any
/// library continuation scheduled here is lost and the operation never
/// completes.
/// </summary>
internal sealed class NonPumpingSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        // Deliberately dropped: the "UI thread" is blocked and never pumps.
    }

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new InvalidOperationException("Synchronous Send is not supported by this context.");
}

/// <summary>
/// Handler that always completes asynchronously on a thread-pool thread, so
/// the library's awaits genuinely suspend and exercise continuation
/// scheduling. Responds per request path.
/// </summary>
internal sealed class AsyncCompletionHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string> _respond;

    public AsyncCompletionHandler(Func<HttpRequestMessage, string> respond) => _respond = respond;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Force true asynchrony; ConfigureAwait(false) here keeps the test
        // helper itself off the non-pumping context.
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_respond(request), Encoding.UTF8, "application/json"),
        };
    }
}

public class ClientSynchronizationContextTests
{
    private const string ProcessJson = """
        {
          "valid": true,
          "rendered": "H",
          "renders": {},
          "assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},
          "inputWasValid": true,
          "assertions": [],
          "errors": [],
          "warnings": [],
          "appliedFixes": [],
          "proposedFixes": [],
          "notices": []
        }
        """;

    private const string HealthJson = """{ "status": "ok", "engineVersion": "1.0.0", "engineSha": "abc", "gitTag": "v1.0.0", "openapiVersion": "1.0.0", "abiVersion": "v1" }""";

    private static void RunUnderBlockedContext(Action scenario)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
                scenario();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();

        Assert.True(
            thread.Join(TimeSpan.FromSeconds(15)),
            "Sync-over-async call deadlocked: a library await captured the " +
            "SynchronizationContext (missing ConfigureAwait(false)).");
        Assert.Null(failure);
    }

    [Fact]
    public void RootClient_SyncOverAsync_DoesNotDeadlock()
    {
        RunUnderBlockedContext(() =>
        {
            var handler = new AsyncCompletionHandler(req =>
                req.RequestUri!.AbsolutePath.EndsWith("/health", StringComparison.Ordinal)
                    ? HealthJson
                    : ContextProbe.Body);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/") };
            using var client = new EzClassifyClient(http);

            var ping = client.PingAsync().GetAwaiter().GetResult();
            Assert.Equal("1.0.0", ping.EngineVersion);
        });
    }

    [Fact]
    public void ContextClient_SyncOverAsync_DoesNotDeadlock()
    {
        RunUnderBlockedContext(() =>
        {
            var handler = new AsyncCompletionHandler(req =>
                req.RequestUri!.AbsolutePath.EndsWith("/scheme/process", StringComparison.Ordinal)
                    ? """{"resolution":{"contextId":"ctx-demo","resolvedVersion":"1.0.0","artifacts":[],"sources":[]},"result":""" + ProcessJson + ""","operationLimitations":[]}"""
                    : ContextProbe.Body);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/") };

            using var client = EzClassifyClient
                .ConnectAsync(http, "ctx-demo", "1.0.0")
                .GetAwaiter()
                .GetResult();

            var result = client.Scheme!.ProcessAsync("HIGH").GetAwaiter().GetResult();
            Assert.True(result.Result.Valid);
        });
    }
}
