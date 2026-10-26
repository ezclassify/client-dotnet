using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace EzClassify.Tests;

/// <summary>One recorded exchange from the vendored wire corpus.</summary>
internal sealed record WireCase(
    string Id,
    string Method,
    string Target,
    JsonNode? RequestBody,
    bool HasRequestBody,
    IReadOnlyDictionary<string, string> RequestHeaders,
    string Credential,
    int Status,
    JsonNode? ResponseBody,
    string ResponseText,
    IReadOnlyDictionary<string, string> ResponseHeaders)
{
    /// <summary>The key a request has to reproduce to be answered with this case.</summary>
    public string MatchKey => WireCorpus.MatchKey(Method, Target, RequestBody, HasRequestBody);
}

/// <summary>
/// The wire corpus vendored under vendor/spec/wire/v1: real exchanges with the
/// runtime, recorded once and replayed by every client in the family.
/// </summary>
internal sealed class WireCorpus
{
    private WireCorpus(IReadOnlyList<WireCase> cases)
    {
        Cases = cases;
        ById = cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    /// <summary>Every recorded case, in corpus order.</summary>
    public IReadOnlyList<WireCase> Cases { get; }

    /// <summary>Cases by id.</summary>
    public IReadOnlyDictionary<string, WireCase> ById { get; }

    /// <summary>This corpus with one case answering <paramref name="body"/> instead of what was recorded.</summary>
    public WireCorpus WithResponseBody(string id, JsonNode body) =>
        new(Cases.Select(c => c.Id == id ? c with { ResponseBody = body, ResponseText = body.ToJsonString() } : c).ToList());

    /// <summary>The vendored corpus, read once per test run.</summary>
    public static WireCorpus Vendored { get; } = Load(Path.Combine(FindRepoRoot(), "vendor", "spec", "wire", "v1", "corpus.json"));

    /// <summary>Read a corpus file.</summary>
    public static WireCorpus Load(string path, string? openApiPath = null)
    {
        var root = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidDataException($"{path} is empty");
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidDataException("Missing corpus directory");
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json"))) ?? throw new InvalidDataException("Empty wire manifest");
        openApiPath ??= Path.Combine(directory, "..", "..", "openapi.yaml");
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(openApiPath))).ToLowerInvariant();
        if (root["corpusVersion"]?.GetValue<string>() != "v1" || manifest["corpusVersion"]?.GetValue<string>() != "v1"
            || manifest["spec"]?["digest"]?.GetValue<string>() != digest
            || manifest["coverage"]?["cases"]?.GetValue<int>() != root["cases"]?.AsArray().Count
            || manifest["contractDivergences"] is not JsonArray divergences || divergences.Count != 0)
            throw new InvalidDataException("Wire provenance or coverage mismatch");
        var cases = new List<WireCase>();
        foreach (var item in root["cases"]!.AsArray())
        {
            var request = item!["request"]!;
            var response = item["response"]!.AsObject();
            // A recorded body of JSON null reads as a null node, the same as no
            // body at all, so the text is kept to replay it as recorded.
            var responseText = response.TryGetPropertyValue("body", out var recordedBody)
                ? recordedBody?.ToJsonString() ?? "null"
                : "";
            cases.Add(new WireCase(
                item["id"]!.GetValue<string>(),
                request["method"]!.GetValue<string>(),
                request["path"]!.GetValue<string>(),
                request["body"]?.DeepClone(),
                request.AsObject().ContainsKey("body"),
                Strings(request["headers"]),
                item["auth"]?["credential"]?.GetValue<string>() ?? "none",
                response["status"]!.GetValue<int>(),
                recordedBody?.DeepClone(),
                responseText,
                Strings(response["headers"])));
        }
        var operations = root["cases"]!.AsArray().Select(item => item?["operationId"]?.GetValue<string>()).Where(op => op is not null).Distinct(StringComparer.Ordinal).Count();
        if (manifest["coverage"]?["operationsCovered"]?.GetValue<int>() != operations)
            throw new InvalidDataException("Wire operation coverage mismatch");
        if (cases.Count == 0 || cases.Any(c => string.IsNullOrEmpty(c.Id)) || cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != cases.Count)
            throw new InvalidDataException("Empty or duplicate wire cases");
        return new WireCorpus(cases);
    }

    /// <summary>
    /// Match key for one exchange: method, canonical target and canonical body,
    /// by the rules the family's replay server matches on. A request with no
    /// body and a request with an empty body are different keys.
    /// </summary>
    public static string MatchKey(string method, string target, JsonNode? body, bool hasBody) =>
        $"{method.ToUpperInvariant()} {CanonicalTarget(target)}\n{(hasBody ? CanonicalBody(body) : "(no body)")}";

    // The owner's parsed JSON numbers are IEEE-754 doubles. Normalize their
    // value before matching, including -0, decimal/exponent aliases and rounding.
    private static string CanonicalBody(JsonNode? node)
    {
        if (node is null) return "null";
        if (node is JsonObject obj)
            return "{" + string.Join(",", obj.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => JsonSerializer.Serialize(kv.Key) + ":" + CanonicalBody(kv.Value))) + "}";
        if (node is JsonArray array)
            return "[" + string.Join(",", array.Select(CanonicalBody)) + "]";
        if (node.GetValueKind() != JsonValueKind.Number) return node.ToJsonString();
        using var document = JsonDocument.Parse(node.ToJsonString());
        var number = document.RootElement.GetDouble();
        if (!double.IsFinite(number)) return "null";
        if (number == 0) return "0";
        var sign = number < 0 ? "-" : "";
        var roundTrip = Math.Abs(number).ToString("R", CultureInfo.InvariantCulture);
        var at = roundTrip.IndexOf('E');
        var coefficient = at < 0 ? roundTrip : roundTrip.Substring(0, at);
        var exponent = at < 0 ? 0 : int.Parse(roundTrip.Substring(at + 1), CultureInfo.InvariantCulture);
        var point = coefficient.IndexOf('.');
        var position = (point < 0 ? coefficient.Length : point) + exponent;
        var digits = coefficient.Replace(".", "");
        var leading = digits.Length - digits.TrimStart('0').Length;
        position -= leading;
        digits = digits.Substring(leading).TrimEnd('0');
        if (position > 0 && position <= 21)
            return sign + (position >= digits.Length
                ? digits + new string('0', position - digits.Length)
                : digits.Insert(position, "."));
        if (position <= 0 && position > -6)
            return sign + "0." + new string('0', -position) + digits;
        var power = position - 1;
        return sign + digits[0] + (digits.Length > 1 ? "." + digits.Substring(1) : "")
            + "e" + (power >= 0 ? "+" : "") + power.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Canonical request target: a trailing slash dropped and query parameters
    /// ordered by name, then value. Percent-encoding is left as written, because
    /// <c>a%2Fb</c> and <c>a/b</c> address different things.
    /// </summary>
    public static string CanonicalTarget(string target)
    {
        var split = target.IndexOf('?');
        var path = split < 0 ? target : target.Substring(0, split);
        if (path.Length == 0) path = "/";
        else if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal)) path = path.Substring(0, path.Length - 1);
        var query = split < 0 ? "" : target.Substring(split + 1);
        if (query.Length == 0) return path;
        var pairs = query.Split('&')
            .Where(pair => pair.Length > 0)
            .Select(pair =>
            {
                var at = pair.IndexOf('=');
                return at < 0 ? (Name: pair, Value: "") : (Name: pair.Substring(0, at), Value: pair.Substring(at + 1));
            })
            .OrderBy(pair => pair.Name, StringComparer.Ordinal)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .Select(pair => pair.Value.Length == 0 ? pair.Name : $"{pair.Name}={pair.Value}");
        return $"{path}?{string.Join("&", pairs)}";
    }

    private static IReadOnlyDictionary<string, string> Strings(JsonNode? node)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (node is JsonObject obj)
            foreach (var (name, value) in obj) map[name] = value?.GetValue<string>() ?? "";
        return map;
    }

    internal static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EzClassify.Rest.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Cannot locate EzClassify.Rest.sln");
    }
}

/// <summary>
/// A transport that answers exactly the named corpus cases, once each, and
/// nothing else. A request that matches no expected case is answered with a
/// 404 and recorded in <see cref="Misses"/> beside the nearest expected case,
/// so a client that changes a path, a method, a body member or the order of an
/// array fails with the request it sent next to the one that was recorded.
/// </summary>
internal sealed class WireReplayHandler : HttpMessageHandler
{
    private const string ContextTokenHeader = "x-ezclassify-context-token";

    private readonly Dictionary<string, Queue<WireCase>> _pending = new(StringComparer.Ordinal);
    private readonly string? _probeTarget;
    private readonly string? _probeVersion;
    private readonly object _gate = new();
    private bool _probeUsed;

    /// <param name="corpus">The corpus to answer from.</param>
    /// <param name="caseIds">The cases this transport answers, each once.</param>
    /// <param name="probe">
    /// The Context a client will bind to, as (contextId, version). Binding probes
    /// <c>GET /v1/ctx/{contextId}/{version}</c>, keeps the resolved version
    /// header, and checks the body against the contract. When that request is not one of
    /// <paramref name="caseIds"/>, it is answered with the smallest description
    /// the contract accepts, carrying the version, because the corpus records the
    /// probe for two Contexts and records operations against seven.
    /// </param>
    /// <param name="answers">
    /// How many requests each case answers. A call that reads the Context
    /// description sends the same request as the binding probe, so it takes two.
    /// </param>
    public WireReplayHandler(WireCorpus corpus, IEnumerable<string> caseIds, (string ContextId, string Version)? probe, int answers = 1)
    {
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in caseIds)
        {
            if (!claimed.Add(id)) throw new ArgumentException($"duplicate case id {id}", nameof(caseIds));
            if (!corpus.ById.TryGetValue(id, out var recorded))
                throw new ArgumentException($"no corpus case with the id {id}", nameof(caseIds));
            if (!_pending.TryGetValue(recorded.MatchKey, out var bucket))
                _pending[recorded.MatchKey] = bucket = new Queue<WireCase>();
            for (var answer = 0; answer < answers; answer++) bucket.Enqueue(recorded);
        }
        if (probe is { } binding)
        {
            _probeTarget = $"/v1/ctx/{binding.ContextId}/{binding.Version}";
            _probeVersion = binding.Version;
        }
    }

    /// <summary>Requests no expected case recorded, each described beside the nearest one that was.</summary>
    public List<string> Misses { get; } = new();

    /// <summary>Header problems on requests that otherwise matched.</summary>
    public List<string> HeaderProblems { get; } = new();

    /// <summary>Expected cases the client never asked for.</summary>
    public IReadOnlyList<string> Unemitted
    {
        get
        {
            lock (_gate) return _pending.Values.SelectMany(bucket => bucket).Select(c => c.Id).ToList();
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var target = request.RequestUri!.PathAndQuery;
        var bodyText = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var body = bodyText is null ? null : JsonNode.Parse(bodyText);
        var key = WireCorpus.MatchKey(request.Method.Method, target, body, request.Content is not null);

        WireCase? recorded = null;
        lock (_gate)
        {
            if (_pending.TryGetValue(key, out var bucket))
            {
                recorded = bucket.Dequeue();
                if (bucket.Count == 0) _pending.Remove(key);
            }
        }

        if (recorded is null)
        {
            if (_probeTarget is not null && request.Method == HttpMethod.Get && target == _probeTarget && bodyText is null)
            {
                lock (_gate)
                {
                    if (!_probeUsed)
                    {
                        _probeUsed = true;
                        return Respond(200, ContextProbe.Body, new Dictionary<string, string> { ["x-ezclassify-resolved-version"] = _probeVersion! });
                    }
                }
            }
            lock (_gate) Misses.Add(DescribeMiss(request.Method.Method, target, bodyText));
            return Respond(404, """{"error":"no_recording"}""", new Dictionary<string, string>());
        }

        if (request.Method == HttpMethod.Get && target == _probeTarget)
            lock (_gate) _probeUsed = true;
        CheckHeaders(request, recorded);
        return Respond(recorded.Status, recorded.ResponseText, recorded.ResponseHeaders);
    }

    private void CheckHeaders(HttpRequestMessage request, WireCase recorded)
    {
        var problems = new List<string>();
        foreach (var (name, expected) in recorded.RequestHeaders)
        {
            var actual = name.Equals("content-type", StringComparison.OrdinalIgnoreCase)
                ? request.Content?.Headers.ContentType?.MediaType
                : request.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;
            // StringContent adds a charset; the recorded JSON media type remains exact.
            if (actual != expected) problems.Add($"{name}: {actual ?? "(none)"}, recorded {expected}");
        }
        var bearer = request.Headers.Authorization?.ToString();
        var carrier = request.Headers.TryGetValues(ContextTokenHeader, out var tokens) ? string.Join(",", tokens) : null;
        switch (recorded.Credential)
        {
            case "context-token":
                if (carrier != "fixture-context-carrier" || bearer is not null) problems.Add("changed Context credentials");
                break;
            case "invalid-bearer":
                if (carrier != "fixture-context-carrier" || bearer != "Bearer fixture-refused-bearer") problems.Add("changed refused credentials");
                break;
            case "none":
                if (carrier is not null || bearer is not null) problems.Add("unexpected credentials");
                break;
            default:
                problems.Add("unknown recorded credential class");
                break;
        }
        if (problems.Count > 0)
            lock (_gate) HeaderProblems.Add($"{recorded.Id}: {string.Join("; ", problems)}");
    }

    private string DescribeMiss(string method, string target, string? body)
    {
        var path = target.Split('?')[0];
        WireCase? nearest;
        lock (_gate)
        {
            var outstanding = _pending.Values.SelectMany(bucket => bucket).ToList();
            nearest = outstanding.FirstOrDefault(c => c.Target.Split('?')[0] == path) ?? outstanding.FirstOrDefault();
        }
        var lines = new StringBuilder()
            .AppendLine($"  emitted:  {method} {target}")
            .AppendLine($"  body:     {body ?? "(none)"}");
        if (nearest is null)
            return lines.Append("  every expected case has already been served").ToString();
        return lines
            .AppendLine($"  nearest:  {nearest.Method} {nearest.Target}   ({nearest.Id})")
            .Append($"  body:     {nearest.RequestBody?.ToJsonString() ?? "(none)"}")
            .ToString();
    }

    internal static HttpResponseMessage Respond(int status, string body, IReadOnlyDictionary<string, string> headers)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
        };
        foreach (var (name, value) in headers)
        {
            if (name.Equals("content-type", StringComparison.OrdinalIgnoreCase))
            {
                response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(value);
                continue;
            }
            if (!response.Headers.TryAddWithoutValidation(name, value))
                response.Content.Headers.TryAddWithoutValidation(name, value);
        }
        return response;
    }
}
