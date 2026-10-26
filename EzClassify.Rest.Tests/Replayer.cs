using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace EzClassify.Tests;

/// <summary>One pre-recorded response entry keyed in the replayer map.</summary>
internal sealed record ReplayerRecording(int StatusCode, JsonNode Body);

/// <summary>
/// Stub HTTP server that serves pre-recorded engine responses during conformance
/// tests.  Implements the replayer-contract defined in
/// vendor/spec/replayer-contract/v1/README.md.
///
/// Lookup algorithm: sha256(normalizedPath + "|" + stableJson(body)).
/// A miss returns HTTP 404 with the envelope: {error, message, path, bodyHash}.
/// </summary>
internal sealed class Replayer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly Dictionary<string, ReplayerRecording> _recordings;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;
    private byte[]? _lastRequestBody;

    private Replayer(HttpListener listener, Dictionary<string, ReplayerRecording> recordings, string url)
    {
        _listener = listener;
        _recordings = recordings;
        Url = url;
        _loopTask = Task.Run(() => ServeLoopAsync(_cts.Token));
    }

    /// <summary>Base URL of the running HTTP server, e.g. "http://127.0.0.1:NNNNN".</summary>
    public string Url { get; }

    /// <summary>Exact body bytes received by the most recent completed request.</summary>
    internal byte[]? LastRequestBody => _lastRequestBody;

    /// <summary>
    /// Creates and immediately starts a Replayer loaded with the given recordings.
    /// Call Dispose when done to shut down the underlying HTTP server.
    /// </summary>
    public static Replayer Start(Dictionary<string, ReplayerRecording> recordings)
    {
        // HttpListener cannot bind port 0, so a free port is found with a
        // TcpListener first and handed over. Another listener in the same test
        // run can take that port in between, so a failed bind tries a fresh one.
        const int Attempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            int port;
            using (var probe = new TcpListener(IPAddress.Loopback, 0))
            {
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
            }

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
            }
            catch (HttpListenerException) when (attempt < Attempts)
            {
                listener.Close();
                continue;
            }

            return new Replayer(listener, recordings, $"http://127.0.0.1:{port}");
        }
    }

    private async Task ServeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().WaitAsync(ct); }
            catch { break; }
            _ = HandleAsync(ctx);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var resp = ctx.Response;

        try
        {
            var rawPath = req.Url?.AbsolutePath ?? "/";
            var normalizedPath = rawPath.TrimEnd('/');
            if (string.IsNullOrEmpty(normalizedPath)) normalizedPath = "/";

            using var bodyStream = new MemoryStream();
            await req.InputStream.CopyToAsync(bodyStream);
            var bodyBytes = bodyStream.ToArray();
            _lastRequestBody = bodyBytes;
            var bodyStr = Encoding.UTF8.GetString(bodyBytes);

            JsonNode? bodyNode;
            try { bodyNode = JsonNode.Parse(bodyStr); }
            catch
            {
                await WriteJsonAsync(resp, 400,
                    """{"error":"invalid_json","message":"request body is not valid JSON"}""");
                return;
            }

            var stableBody = StableJson(bodyNode);
            var hash = Sha256Hex(normalizedPath + "|" + stableBody);

            if (_recordings.TryGetValue(hash, out var rec))
            {
                await WriteJsonAsync(resp, rec.StatusCode, rec.Body.ToJsonString());
            }
            else
            {
                var bodyHash = Sha256Hex(stableBody);
                var miss = JsonSerializer.Serialize(new
                {
                    error = "no_recording",
                    message = $"no recording for path={normalizedPath}",
                    path = normalizedPath,
                    bodyHash,
                });
                await WriteJsonAsync(resp, 404, miss);
            }
        }
        catch (Exception ex)
        {
            try
            {
                await WriteJsonAsync(resp, 500,
                    $"{{\"error\":\"internal\",\"message\":{JsonSerializer.Serialize(ex.Message)}}}");
            }
            catch { /* best-effort */ }
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse resp, int status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        resp.StatusCode = status;
        resp.ContentType = "application/json";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
        resp.Close();
    }

    /// <summary>
    /// Serializes <paramref name="node"/> as compact JSON with all object keys sorted
    /// lexicographically (byte order of their UTF-8 encoding).  Arrays are not
    /// reordered.  This is the canonical form used for the replayer lookup key.
    /// </summary>
    internal static string StableJson(JsonNode? node)
    {
        if (node is null) return "null";

        if (node is JsonObject obj)
        {
            var parts = obj
                .Select(kv => kv.Key)
                .OrderBy(k => k, StringComparer.Ordinal)
                .Select(k => JsonSerializer.Serialize(k) + ":" + StableJson(obj[k]));
            return "{" + string.Join(",", parts) + "}";
        }

        if (node is JsonArray arr)
        {
            var parts = arr.Select(StableJson);
            return "[" + string.Join(",", parts) + "]";
        }

        return node.ToJsonString();
    }

    /// <summary>
    /// Computes the replayer lookup key for a given path and body object.
    /// Exported so conformance tests can pre-populate the recording map with
    /// entries that match the requests the conformance runner will send.
    /// </summary>
    internal static string RecordingKey(string path, JsonNode body)
    {
        var normalizedPath = path.TrimEnd('/');
        return Sha256Hex(normalizedPath + "|" + StableJson(body));
    }

    /// <summary>Returns the lowercase SHA-256 hex digest of the UTF-8 encoding of <paramref name="input"/>.</summary>
    internal static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        try { _loopTask.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _cts.Dispose();
    }
}
