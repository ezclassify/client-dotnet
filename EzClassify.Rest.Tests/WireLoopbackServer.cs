using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EzClassify.Tests;

internal sealed record WireObservedResponse(int Status, string Body, IReadOnlyDictionary<string, string> Headers);

// Reads the actual transport response before the SDK parses or disposes it.
internal sealed class WireObservationHandler : DelegatingHandler
{
    public WireObservationHandler(HttpMessageHandler inner) : base(inner) { }
    public List<WireObservedResponse> Responses { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var headers = response.Headers.Concat(response.Content.Headers)
            .ToDictionary(p => p.Key, p => string.Join(", ", p.Value), StringComparer.OrdinalIgnoreCase);
        Responses.Add(new WireObservedResponse((int)response.StatusCode, body, headers));
        return response;
    }
}

// A socket fixture delegates request matching to the same maintained handler.
// It computes no answers and surfaces every server failure on disposal.
internal sealed class WireLoopbackServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly HttpMessageInvoker _replay;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    public string Url { get; }

    private WireLoopbackServer(HttpListener listener, WireReplayHandler handler, string url)
    {
        _listener = listener;
        _replay = new HttpMessageInvoker(handler, disposeHandler: false);
        Url = url;
        _loop = ServeAsync();
    }

    public static WireLoopbackServer Start(WireReplayHandler handler)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var listener = new HttpListener();
            var url = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(url);
            try { listener.Start(); }
            catch (HttpListenerException) when (attempt < 9) { listener.Close(); continue; }
            return new WireLoopbackServer(listener, handler, url);
        }
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(_stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (HttpListenerException) when (_stop.IsCancellationRequested) { return; }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { return; }
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.HttpMethod), new Uri(new Uri(Url), context.Request.RawUrl ?? "/"));
            if (context.Request.HasEntityBody)
            {
                using var bytes = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(bytes).ConfigureAwait(false);
                request.Content = new ByteArrayContent(bytes.ToArray());
            }
            foreach (var name in context.Request.Headers.AllKeys)
            {
                if (name is null) continue;
                var value = context.Request.Headers[name];
                if (!request.Headers.TryAddWithoutValidation(name, value)) request.Content?.Headers.TryAddWithoutValidation(name, value);
            }
            using var response = await _replay.SendAsync(request, _stop.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsByteArrayAsync(_stop.Token).ConfigureAwait(false);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                context.Response.Headers[header.Key] = string.Join(", ", header.Value);
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body, _stop.Token).ConfigureAwait(false);
            context.Response.Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try
        {
            // Close once; Stop then Close can rebind the released port on managed HttpListener.
            _listener.Close();
            await _loop.ConfigureAwait(false);
        }
        finally { _replay.Dispose(); _stop.Dispose(); }
    }
}
