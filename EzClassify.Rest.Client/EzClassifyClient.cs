using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EzClassify;

/// <summary>Response from <see cref="EzClassifyClient.PingAsync"/>.</summary>
/// <param name="EngineVersion">Engine version currently running on the rest-api.</param>
/// <param name="EngineSha">Git commit SHA of the deployed rest-api build (empty string when unavailable).</param>
/// <param name="GitTag">Git tag of the deployed rest-api build (empty string when unavailable).</param>
public sealed record PingResult(
    string EngineVersion,
    string EngineSha,
    string GitTag
);

/// <summary>Controls response caching on one connected Context client; supplied when connecting and fixed for the client's lifetime.</summary>
/// <param name="Enabled">Whether repeated read operations share validated responses.</param>
/// <param name="CacheAccessDecisions">Whether single-target access decisions may skip a server audit record. Off by default. Connecting with it on requires a concrete Context version; <c>latest</c> or an omitted version is refused.</param>
public sealed record ContextCacheOptions(bool Enabled = true, bool CacheAccessDecisions = false);

/// <summary>
/// HTTP client for a running EzClassify REST API server.
///
/// <para>Uses the remote server's GET discovery and read endpoints and JSON POST
/// operation endpoints; no local runtime is required.</para>
///
/// <para>The root client provides server discovery and health operations.
/// <see cref="ConnectAsync(string, string, string?, CancellationToken)"/> returns a client bound
/// to a server-resolved Context for classification operations, pinned to a
/// concrete version or, with the version omitted or <c>latest</c>, following the
/// version the server chooses.</para>
///
/// <para>Every call finishes within the request timeout, the
/// <see cref="HttpClient.Timeout"/> of the transport: 100 seconds on the
/// <see cref="HttpClient"/> the client creates for a base URL, and whatever
/// a supplied one is set to when the client is built. A supplied client whose
/// timeout is <see cref="Timeout.InfiniteTimeSpan"/>, a common setup when a
/// resilience handler such as Polly owns per-try timeouts, gets the same
/// 100 seconds as the whole-request deadline. The deadline covers
/// sending, the response and the whole body, including a handler or body
/// stream that ignores cancellation. A call that runs past it throws
/// <see cref="EzClassifyClientException"/> whose
/// <see cref="Exception.InnerException"/> is a <see cref="TimeoutException"/>.
/// A caller's <see cref="CancellationToken"/> that fires first still wins and
/// surfaces as <see cref="OperationCanceledException"/>.</para>
/// </summary>
public sealed class EzClassifyClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeSpan _requestTimeout;
    private readonly bool _ownsClient;
    private readonly RestClientCredentials _credentials;
    private readonly ContextResponseCache _responseCache = new();
    // Connection state: what the client holds about the Context it is bound to,
    // apart from operation results. ClearCache leaves it alone, and it has no
    // lifetime of its own. It is dropped only when the composition moves.
    private readonly ContextResponseCache _connectionState = new();
    private readonly bool _cacheEnabled = true;
    private readonly bool _cacheAccessDecisions;
    private int _versionWarnEmitted;

    // Context-binding state. Null on the root discovery client and populated
    // only on a client returned by ConnectAsync.
    private readonly string? _contextId;
    private readonly string? _ctxVersionSegment;
    private string? _resolvedVersion;

    /// <summary>
    /// Concrete version the bound Context resolved to, surfaced from the
    /// <c>X-EzClassify-Resolved-Version</c> response header. Populated at
    /// <see cref="ConnectAsync(string, string, string?, CancellationToken)"/>
    /// time and refreshed when a server response supplies the header. Cached
    /// calls do not contact the server. Null until a response supplies it.
    /// </summary>
    public string? ResolvedVersion => Volatile.Read(ref _resolvedVersion);

    /// <summary>
    /// Discard cached operation results without disconnecting this Context. What
    /// <see cref="DescribeContextAsync"/> holds is connection state, not an
    /// operation result, and stays.
    /// </summary>
    public void ClearCache() => _responseCache.Clear();

    // Test seams. The clock is the only one the cache reads for entry lifetimes;
    // the pending count lets a test see every caller of a concurrent call has
    // joined the in-flight entry.
    internal Func<DateTimeOffset> CacheClock { set => _responseCache.Clock = value; }

    internal int PendingCacheCallers => _responseCache.PendingCallers;

    // The whole-request deadline this client settled on when it was built.
    internal TimeSpan RequestTimeout => _requestTimeout;

    /// <summary>
    /// Scheme-domain operations bound to the connected Context
    /// (<c>/v1/ctx/{ctxId}/{version}/scheme/*</c>). Null on the root discovery client.
    /// </summary>
    public ContextSchemeOps? Scheme { get; }

    /// <summary>
    /// Guide-domain operations bound to the connected Context
    /// (<c>/v1/ctx/{ctxId}/{version}/guide/*</c>). Null on the root discovery client.
    /// </summary>
    public ContextGuideOps? Guide { get; }

    /// <summary>
    /// Access-control operations bound to the connected Context
    /// (<c>/v1/ctx/{ctxId}/{version}/access/*</c>). Null on the root discovery client.
    /// </summary>
    public ContextAccessOps? Access { get; }

    /// <summary>
    /// Crosswalk operations bound to the connected Context
    /// (<c>/v1/ctx/{ctxId}/{version}/crosswalk/*</c>). Null on the root discovery client.
    /// </summary>
    public ContextCrosswalkOps? Crosswalk { get; }

    /// <summary>
    /// Document-level operations bound to the connected Context
    /// (<c>/v1/ctx/{ctxId}/{version}/document/*</c>). Null on the root discovery client.
    /// </summary>
    public ContextDocumentOps? Document { get; }

    /// <summary>
    /// The whole-request deadline when the transport sets none: the timeout of
    /// the <see cref="HttpClient"/> the client creates for a base URL, and of a
    /// supplied one whose timeout is infinite. It matches
    /// <see cref="HttpClient"/>'s own default.
    /// </summary>
    internal static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Create a root discovery client for <paramref name="baseUrl"/>. A new
    /// <see cref="HttpClient"/> is created and owned by this instance.
    /// </summary>
    public EzClassifyClient(string baseUrl)
        : this(baseUrl, RestClientCredentials.None)
    { }

    /// <summary>Create a root discovery client with independent caller and Context credentials.</summary>
    /// <param name="baseUrl">REST API base URL.</param>
    /// <param name="credentials">Caller identity and Context authorization.</param>
    /// <exception cref="ArgumentException"><paramref name="baseUrl"/> is empty or is not an absolute URL.</exception>
    public EzClassifyClient(string baseUrl, RestClientCredentials credentials)
        : this(CreateOwnedHttpClient(baseUrl), ownsClient: true, credentials, DefaultRequestTimeout)
    { }

    // Owned transport. A client that connects once at startup and lives for
    // days would otherwise hold its pooled connections indefinitely and keep
    // targeting a stale address after the rest-api's address moves behind a
    // load balancer or a rolling Kubernetes service. A bounded connection
    // lifetime forces periodic reconnects and DNS re-resolution. Callers who
    // supply their own HttpClient, including through IHttpClientFactory, own
    // this setting themselves. netstandard2.0 has no SocketsHttpHandler, so
    // that target keeps the platform default.
    private static HttpClient CreateOwnedHttpClient(string baseUrl)
    {
        var baseAddress = BaseAddressOf(baseUrl);
#if NET
        var handler = new System.Net.Http.SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        return new HttpClient(handler) { BaseAddress = baseAddress, Timeout = DefaultRequestTimeout };
#else
        return new HttpClient { BaseAddress = baseAddress, Timeout = DefaultRequestTimeout };
#endif
    }

    // Public boundary: an empty or unparseable base URL is a caller mistake,
    // reported as ArgumentException naming the parameter rather than as the
    // NullReferenceException or UriFormatException the raw construction gives.
    private static Uri BaseAddressOf(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("baseUrl must not be empty", nameof(baseUrl));
        if (!Uri.TryCreate(baseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri))
            throw new ArgumentException("baseUrl must be an absolute URL", nameof(baseUrl));
        return uri;
    }

    // The request timeout is the transport's own, read once when the client is
    // built so a later change to the HttpClient cannot lift the bound. Every
    // request is held to it as one deadline, whatever the handler underneath
    // does. An infinite timeout is how IHttpClientFactory setups hand per-try
    // timeouts to a resilience handler such as Polly; the handler bounds each
    // attempt, but nothing would bound the call as a whole, and a stalled
    // server or a handler that ignores cancellation could hold it open forever.
    // Such a client gets the default deadline instead. HttpClient refuses every
    // other value that is not positive, so infinite is the only one that can
    // reach this check.
    private static TimeSpan RequestTimeoutOf(HttpClient http, TimeSpan fallback)
    {
        var timeout = http.Timeout;
        return timeout <= TimeSpan.Zero ? fallback : timeout;
    }

    /// <summary>
    /// Create a client using a supplied <see cref="HttpClient"/>. The caller is
    /// responsible for the lifetime of <paramref name="httpClient"/>. Its
    /// <see cref="HttpClient.Timeout"/> is read now and bounds every request;
    /// an infinite one gets the default 100 seconds.
    /// </summary>
    public EzClassifyClient(HttpClient httpClient)
        : this(httpClient, RestClientCredentials.None)
    { }

    /// <summary>Create a client over a caller-owned transport with independent credentials.</summary>
    /// <param name="httpClient">Caller-owned HTTP transport. Its <see cref="HttpClient.Timeout"/> is read now and bounds every request; an infinite one gets the default 100 seconds.</param>
    /// <param name="credentials">Caller identity and Context authorization.</param>
    public EzClassifyClient(HttpClient httpClient, RestClientCredentials credentials)
        : this(httpClient, ownsClient: false, credentials, DefaultRequestTimeout)
    { }

    // Caller-owned transport with a different fallback deadline, so tests can
    // reach the infinite-timeout path without waiting the full default.
    internal EzClassifyClient(HttpClient httpClient, TimeSpan defaultRequestTimeout)
        : this(httpClient, ownsClient: false, RestClientCredentials.None, defaultRequestTimeout)
    { }

    private EzClassifyClient(
        HttpClient http, bool ownsClient, RestClientCredentials credentials, TimeSpan defaultRequestTimeout)
    {
        if (http is null) throw new ArgumentNullException("httpClient");
        _requestTimeout = RequestTimeoutOf(http, defaultRequestTimeout);
        _http = http;
        _ownsClient = ownsClient;
        _credentials = credentials ?? RestClientCredentials.None;
    }

    // Context-binding constructor. The version segment is the literal that goes
    // into the URL: a concrete semver or "latest", which an omitted version also
    // sends. The runtime treats the two alike (the Context token's default
    // version, else the newest installed) and reports the concrete version back
    // via X-EzClassify-Resolved-Version.
    private EzClassifyClient(
        HttpClient http,
        string contextId,
        string versionSegment,
        bool ownsClient,
        RestClientCredentials credentials,
        ContextCacheOptions cacheOptions)
    {
        if (http is null) throw new ArgumentNullException("httpClient");
        if (string.IsNullOrEmpty(contextId)) throw new ArgumentException("contextId must not be empty", nameof(contextId));
        _requestTimeout = RequestTimeoutOf(http, DefaultRequestTimeout);
        _http = http;
        _ownsClient = ownsClient;
        _credentials = credentials ?? RestClientCredentials.None;
        _contextId = contextId;
        _ctxVersionSegment = versionSegment;
        if (cacheOptions is null) throw new ArgumentNullException(nameof(cacheOptions));
        // An alias that moves can change which version an uncached access decision
        // would have seen, so a cached decision is only sound against a pinned version.
        if (cacheOptions.Enabled && cacheOptions.CacheAccessDecisions && versionSegment == "latest")
            throw new ArgumentException(
                "Access-decision caching requires a concrete Context version.", nameof(cacheOptions));
        _cacheEnabled = cacheOptions.Enabled;
        _cacheAccessDecisions = cacheOptions.CacheAccessDecisions;
        Scheme = new ContextSchemeOps(this);
        Guide = new ContextGuideOps(this);
        Access = new ContextAccessOps(this);
        Crosswalk = new ContextCrosswalkOps(this);
        Document = new ContextDocumentOps(this);
    }

    /// <summary>
    /// Connect to a named, versioned Context on a running EzClassify REST API
    /// and return a context-bound client. Every operation on the returned
    /// client routes through <c>/v1/ctx/{contextId}/{version}/*</c>; request
    /// bodies carry payload only; the context selects the scheme, guides, and
    /// access policy.
    /// </summary>
    /// <param name="baseUrl">Base URL of the REST API, e.g. <c>http://localhost:3000</c>.</param>
    /// <param name="contextId">Published Context id to bind to.</param>
    /// <param name="version">
    /// Concrete semver to pin. Omit it (null or empty) to let the server choose:
    /// the Context token's default version, or the newest published. An omitted
    /// version binds the same way as <c>latest</c>. The resolved concrete version
    /// is available on <see cref="ResolvedVersion"/> after this call.
    /// </param>
    /// <param name="cancellationToken">Cancels the initial context probe.</param>
    /// <exception cref="EzClassifyClientException">The server refused the binding, such as an unknown Context, could not be reached, or did not answer within the request timeout.</exception>
    /// <exception cref="JsonException">The binding probe answered with a body the contract does not allow; <see cref="JsonException.Path"/> names the member, such as <c>$.schemes</c>.</exception>
    public static async Task<EzClassifyClient> ConnectAsync(
        string baseUrl,
        string contextId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        return await ConnectAsync(
            baseUrl, contextId, version, RestClientCredentials.None, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Connect with independent caller identity and Context authorization.</summary>
    /// <param name="baseUrl">REST API base URL.</param>
    /// <param name="contextId">Published Context id.</param>
    /// <param name="version">Concrete version, or <c>null</c> to let the server choose: the Context token's default version, or the newest published.</param>
    /// <param name="credentials">Caller identity and Context authorization.</param>
    /// <param name="cancellationToken">Cancels the initial Context probe.</param>
    /// <returns>A resolved Context-bound client.</returns>
    /// <exception cref="EzClassifyClientException">The server refused the binding, such as an unknown Context, could not be reached, or did not answer within the request timeout.</exception>
    /// <exception cref="JsonException">The binding probe answered with a body the contract does not allow; <see cref="JsonException.Path"/> names the member, such as <c>$.schemes</c>.</exception>
    public static Task<EzClassifyClient> ConnectAsync(
        string baseUrl,
        string contextId,
        string? version,
        RestClientCredentials credentials,
        CancellationToken cancellationToken = default) =>
        ConnectAsync(baseUrl, contextId, version, credentials, new ContextCacheOptions(), cancellationToken);

    /// <summary>Connect with credentials and explicit response-cache options.</summary>
    /// <param name="baseUrl">REST API base URL.</param>
    /// <param name="contextId">Published Context id.</param>
    /// <param name="version">Concrete version, or <c>null</c> to let the server choose: the Context token's default version, or the newest published.</param>
    /// <param name="credentials">Caller identity and Context authorization.</param>
    /// <param name="cacheOptions">Response-cache choices, fixed for the life of the returned client.</param>
    /// <param name="cancellationToken">Cancels the initial Context probe.</param>
    /// <returns>A resolved Context-bound client.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="cacheOptions"/> enables access-decision caching and <paramref name="version"/> is
    /// omitted or <c>latest</c>.
    /// </exception>
    /// <exception cref="EzClassifyClientException">The server refused the binding, such as an unknown Context, could not be reached, or did not answer within the request timeout.</exception>
    /// <exception cref="JsonException">The binding probe answered with a body the contract does not allow; <see cref="JsonException.Path"/> names the member, such as <c>$.schemes</c>.</exception>
    public static async Task<EzClassifyClient> ConnectAsync(
        string baseUrl,
        string contextId,
        string? version,
        RestClientCredentials credentials,
        ContextCacheOptions cacheOptions,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(contextId)) throw new ArgumentException("contextId must not be empty", nameof(contextId));
        var http = CreateOwnedHttpClient(baseUrl);
        return await ConnectOwnedAsync(http, contextId, version, credentials, cacheOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    // Owned-client connect core: the probe can fail (server down, unknown
    // context, cancellation), and the HttpClient created above must not leak
    // its handler and sockets on that path. Internal so the failure-path
    // disposal has direct test coverage.
    internal static async Task<EzClassifyClient> ConnectOwnedAsync(
        HttpClient http,
        string contextId,
        string? version,
        CancellationToken cancellationToken) =>
        await ConnectOwnedAsync(
            http, contextId, version, RestClientCredentials.None, cancellationToken).ConfigureAwait(false);

    internal static async Task<EzClassifyClient> ConnectOwnedAsync(
        HttpClient http,
        string contextId,
        string? version,
        RestClientCredentials credentials,
        CancellationToken cancellationToken) =>
        await ConnectOwnedAsync(
            http, contextId, version, credentials, new ContextCacheOptions(), cancellationToken).ConfigureAwait(false);

    internal static async Task<EzClassifyClient> ConnectOwnedAsync(
        HttpClient http,
        string contextId,
        string? version,
        RestClientCredentials credentials,
        ContextCacheOptions cacheOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            var versionSegment = version is null or "" ? "latest" : version;
            var client = new EzClassifyClient(
                http, contextId, versionSegment, ownsClient: true, credentials, cacheOptions);
            await client.ResolveContextAsync(cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Connect to a named, versioned Context using a caller-supplied
    /// <see cref="HttpClient"/>. The caller owns the client's lifetime, and its
    /// <see cref="HttpClient.Timeout"/>, read now, bounds every request; an
    /// infinite one gets the default 100 seconds. See
    /// <see cref="ConnectAsync(string, string, string?, CancellationToken)"/>.
    /// </summary>
    /// <exception cref="EzClassifyClientException">The server refused the binding, such as an unknown Context, could not be reached, or did not answer within the request timeout.</exception>
    /// <exception cref="JsonException">The binding probe answered with a body the contract does not allow; <see cref="JsonException.Path"/> names the member, such as <c>$.schemes</c>.</exception>
    public static async Task<EzClassifyClient> ConnectAsync(
        HttpClient httpClient,
        string contextId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        return await ConnectAsync(
            httpClient, contextId, version, RestClientCredentials.None, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Connect over a caller-owned transport with independent credentials.</summary>
    /// <param name="httpClient">Caller-owned HTTP transport. Its <see cref="HttpClient.Timeout"/> is read now and bounds every request; an infinite one gets the default 100 seconds.</param>
    /// <param name="contextId">Published Context id.</param>
    /// <param name="version">Concrete version, or <c>null</c> to let the server choose: the Context token's default version, or the newest published.</param>
    /// <param name="credentials">Caller identity and Context authorization.</param>
    /// <param name="cancellationToken">Cancels the initial Context probe.</param>
    /// <returns>A resolved Context-bound client.</returns>
    /// <exception cref="EzClassifyClientException">The server refused the binding, such as an unknown Context, could not be reached, or did not answer within the request timeout.</exception>
    /// <exception cref="JsonException">The binding probe answered with a body the contract does not allow; <see cref="JsonException.Path"/> names the member, such as <c>$.schemes</c>.</exception>
    public static Task<EzClassifyClient> ConnectAsync(
        HttpClient httpClient,
        string contextId,
        string? version,
        RestClientCredentials credentials,
        CancellationToken cancellationToken = default) =>
        ConnectAsync(httpClient, contextId, version, credentials, new ContextCacheOptions(), cancellationToken);

    /// <summary>Connect over a caller-owned transport with credentials and explicit response-cache options.</summary>
    /// <param name="httpClient">Caller-owned HTTP transport. Its <see cref="HttpClient.Timeout"/> is read now and bounds every request; an infinite one gets the default 100 seconds.</param>
    /// <param name="contextId">Published Context id.</param>
    /// <param name="version">Concrete version, or <c>null</c> to let the server choose: the Context token's default version, or the newest published.</param>
    /// <param name="credentials">Caller identity and Context authorization.</param>
    /// <param name="cacheOptions">Response-cache choices, fixed for the life of the returned client.</param>
    /// <param name="cancellationToken">Cancels the initial Context probe.</param>
    /// <returns>A resolved Context-bound client.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="cacheOptions"/> enables access-decision caching and <paramref name="version"/> is
    /// omitted or <c>latest</c>.
    /// </exception>
    /// <exception cref="EzClassifyClientException">The server refused the binding, such as an unknown Context, could not be reached, or did not answer within the request timeout.</exception>
    /// <exception cref="JsonException">The binding probe answered with a body the contract does not allow; <see cref="JsonException.Path"/> names the member, such as <c>$.schemes</c>.</exception>
    public static async Task<EzClassifyClient> ConnectAsync(
        HttpClient httpClient,
        string contextId,
        string? version,
        RestClientCredentials credentials,
        ContextCacheOptions cacheOptions,
        CancellationToken cancellationToken = default)
    {
        if (httpClient is null) throw new ArgumentNullException(nameof(httpClient));
        var versionSegment = version is null or "" ? "latest" : version;
        var client = new EzClassifyClient(
            httpClient, contextId, versionSegment, ownsClient: false, credentials, cacheOptions);
        await client.ResolveContextAsync(cancellationToken).ConfigureAwait(false);
        return client;
    }

    // Probe the context describe endpoint (GET /v1/ctx/{id}/{version}) to
    // verify the binding exists and capture the resolved concrete version,
    // which rides the response header SendCoreAsync records. The body is not
    // kept, but it is still the server's answer about the binding, so one that
    // lacks a member the contract requires fails the connect.
    private async Task ResolveContextAsync(CancellationToken cancellationToken)
    {
        var path = $"v1/ctx/{Uri.EscapeDataString(_contextId!)}/{Uri.EscapeDataString(_ctxVersionSegment!)}";
        var json = await SendCoreAsync(HttpMethod.Get, path, body: null, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        ResponseCheck.Require("CtxDescribeResponseBody", document.RootElement);
    }

    /// <summary>
    /// Describe the Context this client is bound to: its composed scheme,
    /// every scheme it holds, its layers, guides, access policy and crosswalks,
    /// each with the version the Context declares and the concrete version that
    /// answered. Record these component versions to say which artifacts a
    /// decision was made against.
    ///
    /// <para>The description is connection state, not a cached operation
    /// result. It is read on first use and held; callers that race the first
    /// call share one request, and canceling one caller leaves the others
    /// untouched. <see cref="ClearCache"/> does not drop it. On a
    /// <c>latest</c> binding it is dropped when a response names a different
    /// resolved version, so the next call describes the moved composition. A
    /// failed or malformed read is not held.</para>
    /// </summary>
    /// <param name="cancellationToken">Cancels this caller's wait.</param>
    /// <returns>The resolved composition of the bound Context.</returns>
    /// <exception cref="InvalidOperationException">This is the root discovery client, which is bound to no Context.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    /// <exception cref="JsonException">The response is missing a member the contract requires, at any depth; <see cref="JsonException.Path"/> names it, such as <c>$.schemes[0].roles</c>.</exception>
    public Task<ContextDescription> DescribeContextAsync(CancellationToken cancellationToken = default)
    {
        if (_contextId is null)
            throw new InvalidOperationException(
                "DescribeContextAsync requires a Context-bound client; create one with ConnectAsync.");
        var path = $"v1/ctx/{Uri.EscapeDataString(_contextId)}/{Uri.EscapeDataString(_ctxVersionSegment!)}";
        return _connectionState.GetAsync(
            "context.describe",
            ttl: TimeSpan.Zero,
            pinned: true,
            async shared => ContextResultParser.ParseContextDescription(
                await SendCoreAsync(HttpMethod.Get, path, body: null, shared).ConfigureAwait(false)),
            cancellationToken);
    }

    /// <summary>List every Context id the caller may use.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context ids, in the order the server lists them.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    /// <exception cref="JsonException">The response is not the shape the contract declares; <see cref="JsonException.Path"/> names the member, such as <c>$.contexts</c>.</exception>
    public async Task<IReadOnlyList<string>> ListContextsAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await SendCoreAsync(HttpMethod.Get, "v1/ctx", body: null, cancellationToken)
            .ConfigureAwait(false);
        return ReadStringList(json, "CtxListResponseBody", "contexts");
    }

    /// <summary>List every published version of one Context that the caller may use, highest first.</summary>
    /// <param name="contextId">Published Context id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The concrete versions, in the order the server lists them.</returns>
    /// <exception cref="ArgumentException"><paramref name="contextId"/> is empty.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    /// <exception cref="JsonException">The response is not the shape the contract declares; <see cref="JsonException.Path"/> names the member, such as <c>$.versions</c>.</exception>
    public async Task<IReadOnlyList<string>> ListContextVersionsAsync(
        string contextId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(contextId))
            throw new ArgumentException("contextId must not be empty", nameof(contextId));
        var path = $"v1/ctx/{Uri.EscapeDataString(contextId)}";
        var json = await SendCoreAsync(HttpMethod.Get, path, body: null, cancellationToken)
            .ConfigureAwait(false);
        return ReadStringList(json, "CtxVersionsResponseBody", "versions");
    }

    // Discovery answers are a required array of strings under one member.
    private static IReadOnlyList<string> ReadStringList(string json, string component, string member)
    {
        using var document = JsonDocument.Parse(json);
        ResponseCheck.Require(component, document.RootElement);
        var result = new List<string>();
        var index = 0;
        foreach (var item in document.RootElement.GetProperty(member).EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new JsonException($"Response member $.{member}[{index}] must be a string.", $"$.{member}[{index}]", null, null);
            result.Add(item.GetString()!);
            index++;
        }
        return result.AsReadOnly();
    }

    /// <summary>
    /// Return the engine spec version this client was built against.
    ///
    /// <para>This is a compile-time constant taken from <c>vendor/spec/MANIFEST.json</c>
    /// at publish time. It equals the rest-api version this client is intended to
    /// communicate with.</para>
    /// </summary>
    public string EngineSpecVersion => SpecVersion.ClientSpecVersion;

    // Receives the one-time server-version-skew notice raised by PingAsync.
    // Internal so the public surface carries no logging hook: a host observes the
    // notice through System.Diagnostics.Trace listeners, and the tests observe
    // it here. The callback runs on the thread that called PingAsync.
    internal Action<string>? DiagnosticSink { get; set; }

    /// <summary>
    /// Ping the live rest-api and return its wire version, git SHA, and git tag.
    ///
    /// <para>On the first call, if the rest-api major.minor version differs from
    /// <see cref="EngineSpecVersion"/>, a one-time notice is written through
    /// <see cref="System.Diagnostics.Trace"/>, which is compiled into Release
    /// builds and observable with any trace listener.</para>
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The versions the server reports.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    /// <exception cref="JsonException">The response lacks a member the contract requires; <see cref="JsonException.Path"/> names it, such as <c>$.engineVersion</c>.</exception>
    public async Task<PingResult> PingAsync(CancellationToken cancellationToken = default)
    {
        var responseBody = await SendCoreAsync(
            HttpMethod.Get, "v1/health", body: null, cancellationToken).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;
        ResponseCheck.Require("HealthResponse", root);
        var result = new PingResult(
            HealthString(root, "engineVersion"), HealthString(root, "engineSha"), HealthString(root, "gitTag"));

        if (Interlocked.CompareExchange(ref _versionWarnEmitted, 1, 0) == 0)
        {
            if (MajorMinor(SpecVersion.ClientSpecVersion) != MajorMinor(result.EngineVersion))
            {
                var notice =
                    $"ezclassify-dotnet v{SpecVersion.ClientSpecVersion} is intended for use with " +
                    $"rest-api v{SpecVersion.ClientSpecVersion}. " +
                    $"Connected to rest-api v{result.EngineVersion}. Mixed minor versions are unsupported.";

                // Debug.WriteLine is [Conditional("DEBUG")], so it is compiled
                // out of the Release build that gets packed and a package
                // consumer would never see this. Trace survives Release.
                var sink = DiagnosticSink;
                if (sink is not null) sink(notice);
                else System.Diagnostics.Trace.WriteLine(notice);
            }
        }

        return result;
    }

    private static string HealthString(JsonElement root, string member)
    {
        var value = root.GetProperty(member);
        if (value.ValueKind != JsonValueKind.String)
            throw new JsonException($"Response member $.{member} must be a string.", $"$.{member}", null, null);
        return value.GetString()!;
    }

    private static string MajorMinor(string version)
    {
        var parts = version.Split('.');
        var major = parts.Length > 0 ? parts[0] : "0";
        var minor = parts.Length > 1 ? parts[1] : "0";
        return $"{major}.{minor}";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }

    // --- Transport -----------------------------------------------------------

    /// <summary>
    /// POST a payload-only body to a context op and return the response body.
    /// <paramref name="opPath"/> may carry an already-appended query string.
    /// </summary>
    internal Task<string> PostCtxAsync(string opPath, object body, CancellationToken cancellationToken)
        => SendCoreAsync(HttpMethod.Post, BuildCtxOpPath(opPath), body, cancellationToken);

    /// <summary>GET a context op (query already appended to <paramref name="opPathWithQuery"/>) and return the response body.</summary>
    internal Task<string> GetCtxAsync(string opPathWithQuery, CancellationToken cancellationToken)
        => SendCoreAsync(HttpMethod.Get, BuildCtxOpPath(opPathWithQuery), body: null, cancellationToken);

    private sealed record SerializedJsonBody(string Json);

    internal Task<T> CachedPostCtxAsync<T>(
        string operation, string opPath, object body, Func<string, T> parse, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(body);
        return CachedCtxAsync(operation, opPath, json, parse, cancellationToken,
            shared => SendCoreAsync(HttpMethod.Post, BuildCtxOpPath(opPath), new SerializedJsonBody(json), shared));
    }

    internal Task<T> CachedGetCtxAsync<T>(
        string operation, string opPath, Func<string, T> parse, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return CachedCtxAsync(operation, opPath, "null", parse, cancellationToken,
            shared => GetCtxAsync(opPath, shared));
    }

    internal Task<T> DecisionPostCtxAsync<T>(
        string operation, string opPath, object body, Func<string, T> parse, CancellationToken cancellationToken)
    {
        if (_cacheAccessDecisions && _cacheEnabled)
            return CachedPostCtxAsync(operation, opPath, body, parse, cancellationToken);
        return DirectPostCtxAsync(opPath, body, parse, cancellationToken);
    }

    private async Task<T> DirectPostCtxAsync<T>(
        string opPath, object body, Func<string, T> parse, CancellationToken cancellationToken)
    {
        var json = await PostCtxAsync(opPath, body, cancellationToken).ConfigureAwait(false);
        return parse(json);
    }

    private Task<T> CachedCtxAsync<T>(
        string operation, string opPath, string keyBody, Func<string, T> parse, CancellationToken caller,
        Func<CancellationToken, Task<string>> send)
    {
        if (!_cacheEnabled)
            return ParseAsync(send(caller), parse);
        var ttl = operation switch
        {
            "scheme.scan" => TimeSpan.FromMinutes(2),
            "scheme.describeConcept" or "access.describeAllAccessRules" => TimeSpan.FromHours(1),
            _ => TimeSpan.FromMinutes(5),
        };
        var key = operation + "|ctx:" + _contextId + "@" + _ctxVersionSegment + "|" + opPath + "|"
            + ContextResponseCache.Canonicalize(keyBody);
        return _responseCache.GetAsync(key, ttl, _ctxVersionSegment != "latest",
            async shared => parse(await send(shared).ConfigureAwait(false)), caller);
    }

    private static async Task<T> ParseAsync<T>(Task<string> response, Func<string, T> parse) =>
        parse(await response.ConfigureAwait(false));

    private string BuildCtxOpPath(string opPath) =>
        $"v1/ctx/{Uri.EscapeDataString(_contextId!)}/{Uri.EscapeDataString(_ctxVersionSegment!)}/{opPath}";

    // Single transport shared by discovery, health, and context-bound
    // operations: JSON body, response disposal, cancellation, the request
    // deadline, and error mapping. One budget covers sending, the response
    // headers and the whole body, so no stage gets a fresh timeout.
    private async Task<string> SendCoreAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        using var budget = new RequestBudget(relativePath, _requestTimeout, cancellationToken);
        using var request = new HttpRequestMessage(method, relativePath);
        _credentials.Apply(
            request,
            relativePath.Equals("v1/ctx", StringComparison.Ordinal)
                || relativePath.StartsWith("v1/ctx/", StringComparison.Ordinal));
        if (body is not null)
        {
            request.Content = new StringContent(
                body is SerializedJsonBody serialized ? serialized.Json : JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await budget.RunAsync(token => _http.SendAsync(request, token)).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new EzClassifyClientException($"Connection error: {ex.Message}", ex);
        }

        try
        {
            // HttpClient's default completion mode has already buffered the body
            // inside SendAsync, under this budget. The read still runs under it
            // so content that was not buffered, such as from an HttpClient
            // subclass, cannot outlast the deadline either.
            var responseBody = await budget.RunAsync(token => response.Content.ReadAsStringAsync(token))
                .ConfigureAwait(false);
            CaptureResolvedVersion(response);
            if (!response.IsSuccessStatusCode)
            {
                // A held answer must not outlive the server refusing this caller.
                // A deployment running authentication refuses an unauthenticated
                // request with a 404 carrying exactly {"error":"not_found"}, so that
                // body counts as a refusal too. Credentials can come from a caller's
                // own HttpClient or handler, which this client cannot see, so the
                // check does not depend on RestClientCredentials. A not-found that
                // names the missing thing carries more fields and keeps the cache.
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    || (response.StatusCode == System.Net.HttpStatusCode.NotFound && IsDenialShapedBody(responseBody)))
                {
                    _responseCache.Clear();
                }
                throw EzClassifyClientException.FromResponse(
                    (int)response.StatusCode, responseBody, response.Headers.RetryAfter);
            }
            return responseBody;
        }
        finally
        {
            // A caller-supplied content implementation can block in Dispose.
            // Always start cleanup, including after a body timeout, but wait
            // only within the same request budget. Observe a later failure
            // even when the budget was already spent before this stage began.
            var disposal = Task.Run(() => { response.Dispose(); return true; });
            _ = disposal.ContinueWith(
                static failed => { _ = failed.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            await budget.RunAsync(_ => disposal).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether <paramref name="body"/> is exactly the runtime's constant denial
    /// body: one member, <c>error</c>, whose value is <c>not_found</c>.
    /// </summary>
    private static bool IsDenialShapedBody(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var members = 0;
            var denial = false;
            foreach (var member in root.EnumerateObject())
            {
                members++;
                denial = member.NameEquals("error")
                    && member.Value.ValueKind == JsonValueKind.String
                    && member.Value.ValueEquals("not_found");
            }
            return members == 1 && denial;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void CaptureResolvedVersion(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-EzClassify-Resolved-Version", out var values))
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrEmpty(v))
                {
                    var previous = Interlocked.Exchange(ref _resolvedVersion, v);
                    if (previous is not null && previous != v)
                    {
                        _responseCache.Clear();
                        _connectionState.Clear();
                    }
                    return;
                }
            }
        }
    }

}

/// <summary>Thrown when the EzClassify REST API returns an error or cannot be reached.</summary>
public sealed class EzClassifyClientException : Exception
{
    /// <summary>
    /// The HTTP status code returned by the REST API, or <c>null</c> when no HTTP
    /// status applies (e.g. a connection failure before any response was received).
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>Stable engine error code from the response body, when supplied.</summary>
    public string? Code { get; }

    /// <summary>Stable operation-specific error detail from the response body, when supplied.</summary>
    public string? Detail { get; }

    /// <summary>
    /// How long the server asks the caller to wait before sending another
    /// request, or <c>null</c> when it named no wait. Every operation can answer
    /// 429 when the rate limit is spent, with the wait in seconds in the
    /// <c>Retry-After</c> header and in the body's <c>retryAfter</c>. The header
    /// wins when both are present, and its HTTP-date form reads as the time left
    /// until that date. The client never waits or retries by itself; honoring
    /// this value, and for how long, is the caller's decision.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Exact response body returned by the REST API, when available. The message
    /// carries the status code plus the server's error code and detail when it
    /// sends them, and a short body excerpt otherwise, so read this property when
    /// the full body is wanted; it can echo request content and belongs in a log
    /// only by deliberate choice.
    /// </summary>
    public string? ResponseBody { get; }

    /// <summary>Create an exception with <paramref name="message"/>.</summary>
    public EzClassifyClientException(string message) : base(message) { }

    /// <summary>Create an exception with <paramref name="message"/> and the HTTP <paramref name="statusCode"/> that caused it.</summary>
    public EzClassifyClientException(string message, int? statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>Create an exception wrapping an inner <see cref="Exception"/>.</summary>
    public EzClassifyClientException(string message, Exception inner) : base(message, inner) { }

    /// <summary>Create an exception with the HTTP <paramref name="statusCode"/> that caused it, wrapping an inner <see cref="Exception"/>.</summary>
    public EzClassifyClientException(string message, int? statusCode, Exception inner) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    internal EzClassifyClientException(
        string message, int statusCode, string responseBody, string? code, string? detail, TimeSpan? retryAfter)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Code = code;
        Detail = detail;
        RetryAfter = retryAfter;
    }

    internal static EzClassifyClientException FromResponse(
        int statusCode, string responseBody, System.Net.Http.Headers.RetryConditionHeaderValue? retryHeader = null)
    {
        string? code = null;
        string? detail = null;
        TimeSpan? bodyRetryAfter = null;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) code = c.GetString();
                if (root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String) detail = d.GetString();
                if (root.TryGetProperty("retryAfter", out var r) && r.ValueKind == JsonValueKind.Number
                    && r.TryGetDouble(out var seconds))
                    bodyRetryAfter = FromSeconds(seconds);
            }
        }
        catch (JsonException) { }
        var retryAfter = FromHeader(retryHeader) ?? bodyRetryAfter;
        // Prefer the server's own error vocabulary for the message and fall back
        // to a short body excerpt when the server sends neither field. The full
        // body always stays on ResponseBody so a caller logging Message keeps at
        // most an excerpt of whatever the server echoed back.
        var summary = (code, detail) switch
        {
            (not null, not null) => $"{code}: {detail}",
            (not null, null) => code,
            (null, not null) => detail,
            _ => Truncate(responseBody),
        };
        return new EzClassifyClientException(
            $"REST API error {statusCode}: {summary}", statusCode, responseBody, code, detail, retryAfter);
    }

    // The header is a delta in seconds or an HTTP-date; a date already past
    // reads as no wait left. A negative delta, like a body value that is
    // negative or too large for a TimeSpan, names no usable wait.
    private static TimeSpan? FromHeader(System.Net.Http.Headers.RetryConditionHeaderValue? header)
    {
        if (header is null) return null;
        if (header.Delta is { } delta) return delta < TimeSpan.Zero ? null : delta;
        if (header.Date is { } date)
        {
            var left = date - DateTimeOffset.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
        return null;
    }

    private static TimeSpan? FromSeconds(double seconds) =>
        double.IsNaN(seconds) || seconds < 0 || seconds >= TimeSpan.MaxValue.TotalSeconds
            ? null
            : TimeSpan.FromSeconds(seconds);

    private const int BodyExcerptLength = 200;

    private static string Truncate(string body)
    {
        var text = body.Trim();
        if (text.Length == 0) return "(no response body)";
        return text.Length <= BodyExcerptLength
            ? text
            : text.Substring(0, BodyExcerptLength) + "... (truncated, see ResponseBody)";
    }
}
