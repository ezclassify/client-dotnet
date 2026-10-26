using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

// A handler written without regard for cancellation: it never answers and
// never looks at its token, so HttpClient's own timeout cannot end the wait.
internal sealed class CancellationIgnoringHandler : HttpMessageHandler
{
    private readonly TaskCompletionSource<HttpResponseMessage> _never = new();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) => _never.Task;
}

// Answers the Context binding probe, then hangs every later request the way
// CancellationIgnoringHandler does.
internal sealed class ProbeThenHangHandler : HttpMessageHandler
{
    private readonly TaskCompletionSource<HttpResponseMessage> _never = new();
    private readonly TaskCompletionSource _hung = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _requests;

    /// <summary>Completes when the first request after the probe arrives.</summary>
    public Task Hung => _hung.Task;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _requests) > 1)
        {
            _hung.TrySetResult();
            return _never.Task;
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ContextProbe.Body, Encoding.UTF8, "application/json"),
        });
    }
}

// A real HTTP server on a loopback socket that sends a 200 with its headers
// and the first bytes of the body, then goes silent with the connection open.
internal sealed class StalledBodyServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public StalledBodyServer()
    {
        _listener.Start();
        _ = ServeAsync();
    }

    public Uri BaseAddress => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

    private async Task ServeAsync()
    {
        try
        {
            using var socket = await _listener.AcceptSocketAsync(_stop.Token);
            var buffer = new byte[8192];
            await socket.ReceiveAsync(buffer, SocketFlags.None, _stop.Token);
            var head = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 1000\r\n\r\n{\"status\":";
            await socket.SendAsync(Encoding.ASCII.GetBytes(head), SocketFlags.None, _stop.Token);
            await Task.Delay(Timeout.Infinite, _stop.Token);
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}

/// <summary>
/// Every call finishes within the request timeout, whatever the server or the
/// caller's transport does, and fails as a timeout rather than hanging.
/// </summary>
public class RequestTimeoutTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(300);

    // Long enough that only a hang reaches it, short enough that a regression
    // fails the test instead of stalling the suite.
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    private static async Task<Exception> FailureWithin(TimeSpan limit, Func<Task> call)
    {
        var watch = Stopwatch.StartNew();
        var error = await Record.ExceptionAsync(() => call().WaitAsync(Hang));
        Assert.NotNull(error);
        Assert.IsNotType<TimeoutException>(error);
        Assert.True(watch.Elapsed < limit, $"the call took {watch.Elapsed}, past {limit}");
        return error;
    }

    private static void AssertTimedOut(Exception error)
    {
        var timeout = Assert.IsType<EzClassifyClientException>(error);
        Assert.IsType<TimeoutException>(timeout.InnerException);
        Assert.Null(timeout.StatusCode);
        Assert.Contains("timed out after 300 ms", timeout.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ResponseDisposalCannotOutlastTheRequest(bool errorResponse, bool callerCancels)
    {
        using var release = new ManualResetEventSlim();
        var content = new DisposalContent(release);
        using var http = new HttpClient(new DisposalHandler(content,
            errorResponse ? HttpStatusCode.BadRequest : HttpStatusCode.OK))
        {
            BaseAddress = new Uri("http://localhost:8080/"),
            Timeout = callerCancels ? TimeSpan.FromSeconds(30) : TimeSpan.FromMilliseconds(50),
        };
        using var client = new EzClassifyClient(http);
        using var caller = new CancellationTokenSource();
        var call = Task.Run(() => client.ListContextsAsync(caller.Token));
        try
        {
            await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (callerCancels) caller.Cancel();
            var error = await Record.ExceptionAsync(() => call.WaitAsync(TimeSpan.FromMilliseconds(500)));
            if (callerCancels)
            {
                var canceled = Assert.IsAssignableFrom<OperationCanceledException>(error);
                Assert.Equal(caller.Token, canceled.CancellationToken);
            }
            else
            {
                var timeout = Assert.IsType<EzClassifyClientException>(error);
                Assert.IsType<TimeoutException>(timeout.InnerException);
                Assert.Contains("timed out after 50 ms", timeout.Message);
            }
            Assert.False(content.Finished.Task.IsCompleted);
            Assert.Equal(1, content.Disposals);
        }
        finally
        {
            release.Set();
            await content.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Record.ExceptionAsync(() => call);
        }
        Assert.Equal(1, content.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalResponseDisposalRunsOnceAndPreservesItsError(bool throws)
    {
        var expected = throws ? new InvalidOperationException("dispose failed") : null;
        var content = new DisposalContent(error: expected);
        using var http = new HttpClient(new DisposalHandler(content, HttpStatusCode.OK))
        { BaseAddress = new Uri("http://localhost:8080/") };
        using var client = new EzClassifyClient(http);

        var error = await Record.ExceptionAsync(() => client.ListContextsAsync());

        Assert.Same(expected, error);
        Assert.Equal(1, content.Disposals);
    }

    private sealed class DisposalHandler : HttpMessageHandler
    {
        private readonly HttpContent _content;
        private readonly HttpStatusCode _status;

        internal DisposalHandler(HttpContent content, HttpStatusCode status)
        { _content = content; _status = status; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status) { Content = _content });
    }

    private sealed class DisposalContent : StringContent
    {
        private readonly ManualResetEventSlim? _release;
        private readonly Exception? _error;
        private int _disposals;
        internal int Disposals => Volatile.Read(ref _disposals);
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal DisposalContent(ManualResetEventSlim? release = null, Exception? error = null)
            : base("{\"contexts\":[]}", Encoding.UTF8, "application/json")
        { _release = release; _error = error; }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            Interlocked.Increment(ref _disposals);
            Started.TrySetResult();
            try
            {
                _release?.Wait();
                base.Dispose(disposing);
                if (_error is not null) throw _error;
            }
            finally { Finished.TrySetResult(); }
        }
    }

    [Fact]
    public async Task ABodyThatStallsAfterTheHeadersTimesOut()
    {
        using var server = new StalledBodyServer();
        // The caller lifts its transport's timeout after handing it over; the
        // client keeps the one it read when it was built.
        using var http = new HttpClient { BaseAddress = server.BaseAddress, Timeout = Budget };
        using var client = new EzClassifyClient(http);
        http.Timeout = Timeout.InfiniteTimeSpan;

        AssertTimedOut(await FailureWithin(TimeSpan.FromSeconds(3), () => client.PingAsync()));
    }

    [Fact]
    public async Task ATransportThatIgnoresCancellationStillTimesOut()
    {
        using var http = new HttpClient(new CancellationIgnoringHandler())
        { BaseAddress = new Uri("http://localhost:8080/"), Timeout = Budget };
        using var client = new EzClassifyClient(http);

        AssertTimedOut(await FailureWithin(TimeSpan.FromSeconds(3), () => client.ListContextsAsync()));
    }

    [Fact]
    public async Task TheBindingProbeIsHeldToTheTimeout()
    {
        using var http = new HttpClient(new CancellationIgnoringHandler())
        { BaseAddress = new Uri("http://localhost:8080/"), Timeout = Budget };

        AssertTimedOut(await FailureWithin(
            TimeSpan.FromSeconds(3), () => EzClassifyClient.ConnectAsync(http, "ctx", "1.0.0")));
    }

    [Fact]
    public async Task CallerCancellationStaysCancellationOverATransportThatIgnoresIt()
    {
        using var http = new HttpClient(new CancellationIgnoringHandler())
        { BaseAddress = new Uri("http://localhost:8080/"), Timeout = TimeSpan.FromSeconds(30) };
        using var client = new EzClassifyClient(http);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var error = await FailureWithin(TimeSpan.FromSeconds(3), () => client.PingAsync(cts.Token));

        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(cts.Token, canceled.CancellationToken);
    }

    [Fact]
    public async Task ACallerWhoJoinsAnInFlightRequestSharesItsDeadline()
    {
        var timeout = TimeSpan.FromSeconds(3);
        var handler = new ProbeThenHangHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/"), Timeout = timeout };
        using var ctx = await EzClassifyClient.ConnectAsync(http, "ctx", "1.0.0");
        var clock = Stopwatch.StartNew();

        var first = ctx.Scheme!.ValidateAsync("INTERNAL");
        await handler.Hung.WaitAsync(Hang);
        await Task.Delay(TimeSpan.FromSeconds(1));
        var second = ctx.Scheme.ValidateAsync("INTERNAL");
        Assert.Equal(2, ctx.PendingCacheCallers);

        var failures = await Task.WhenAll(
            FailedAt(first, clock), FailedAt(second, clock)).WaitAsync(Hang);

        foreach (var (error, _) in failures)
            Assert.IsType<TimeoutException>(Assert.IsType<EzClassifyClientException>(error).InnerException);
        // Both end on the one deadline; a fresh one for the joined caller would
        // land it at least a second after the first.
        var apart = (failures[1].At - failures[0].At).Duration();
        Assert.True(apart < TimeSpan.FromMilliseconds(500), $"the two callers failed {apart} apart");
    }

    private static async Task<(Exception? Error, TimeSpan At)> FailedAt(Task call, Stopwatch clock)
    {
        var error = await Record.ExceptionAsync(() => call);
        return (error, clock.Elapsed);
    }

    [Fact]
    public async Task ASuppliedClientWithNoTimeoutGetsTheDefaultDeadline()
    {
        var handler = new MockHttpHandler(ContextProbe.Body);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/"), Timeout = Timeout.InfiniteTimeSpan };

        using var root = new EzClassifyClient(http);
        using var withCredentials = new EzClassifyClient(http, RestClientCredentials.None);
        using var ctx = await EzClassifyClient.ConnectAsync(http, "ctx", "1.0.0");
        using var owned = new EzClassifyClient("http://localhost:8080");

        Assert.Equal(TimeSpan.FromSeconds(100), root.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(100), withCredentials.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(100), ctx.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(100), owned.RequestTimeout);
    }

    [Fact]
    public void ASuppliedClientWithAFiniteTimeoutKeepsIt()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://localhost:8080/"), Timeout = TimeSpan.FromSeconds(7) };
        using var client = new EzClassifyClient(http, defaultRequestTimeout: Budget);

        Assert.Equal(TimeSpan.FromSeconds(7), client.RequestTimeout);
    }

    [Fact]
    public async Task AStalledServerTimesOutAtTheDefaultDeadlineWhenTheClientSetsNone()
    {
        using var server = new StalledBodyServer();
        using var http = new HttpClient { BaseAddress = server.BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using var client = new EzClassifyClient(http, defaultRequestTimeout: Budget);

        AssertTimedOut(await FailureWithin(TimeSpan.FromSeconds(3), () => client.PingAsync()));
    }

    [Fact]
    public async Task APerTryTimeoutHandlerThatRejectsEarlierStillWins()
    {
        // The IHttpClientFactory and Polly setup: no HttpClient timeout, and a
        // handler that bounds each attempt and reports the overrun as its own
        // exception, the way Polly's TimeoutRejectedException does.
        var perTry = new PerTryTimeoutHandler(TimeSpan.FromMilliseconds(100), reject: true)
        { InnerHandler = new NeverRespondingHandler() };
        using var http = new HttpClient(perTry) { BaseAddress = new Uri("http://localhost:8080/"), Timeout = Timeout.InfiniteTimeSpan };
        using var client = new EzClassifyClient(http, defaultRequestTimeout: Hang);

        var error = await FailureWithin(TimeSpan.FromSeconds(3), () => client.PingAsync());

        Assert.IsType<PerTryTimeoutRejectedException>(error);
    }

    [Fact]
    public async Task APerTryTimeoutHandlerThatCancelsEarlierEndsTheCallBeforeTheDeadline()
    {
        // A hand-rolled per-try timeout that lets its own cancellation out. It
        // is a timeout, and the message must not claim the client's deadline,
        // which has not passed.
        var perTry = new PerTryTimeoutHandler(TimeSpan.FromMilliseconds(100), reject: false)
        { InnerHandler = new NeverRespondingHandler() };
        using var http = new HttpClient(perTry) { BaseAddress = new Uri("http://localhost:8080/"), Timeout = Timeout.InfiniteTimeSpan };
        using var client = new EzClassifyClient(http, defaultRequestTimeout: Hang);

        var error = await FailureWithin(TimeSpan.FromSeconds(3), () => client.PingAsync());

        var timeout = Assert.IsType<EzClassifyClientException>(error);
        Assert.IsType<TimeoutException>(timeout.InnerException);
        Assert.Equal("Request to v1/health was canceled by the transport before the 10000 ms deadline", timeout.Message);
    }
}

/// <summary>Stands in for Polly's TimeoutRejectedException without the dependency.</summary>
internal sealed class PerTryTimeoutRejectedException : Exception
{
    public PerTryTimeoutRejectedException(Exception inner) : base("The attempt timed out.", inner) { }
}

// Bounds each attempt the way a resilience pipeline's per-try timeout does:
// its own token is canceled after the limit, and the overrun either becomes a
// rejection exception or escapes as the cancellation itself.
internal sealed class PerTryTimeoutHandler : DelegatingHandler
{
    private readonly TimeSpan _limit;
    private readonly bool _reject;

    public PerTryTimeoutHandler(TimeSpan limit, bool reject)
    {
        _limit = limit;
        _reject = reject;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(_limit);
        try
        {
            return await base.SendAsync(request, attempt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException canceled) when (_reject && !cancellationToken.IsCancellationRequested)
        {
            throw new PerTryTimeoutRejectedException(canceled);
        }
    }
}
