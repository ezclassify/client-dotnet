/**
 * EzClassify .NET Server Tour
 *
 * Walks the main EzClassifyClient calls. By default it runs against the Cedar
 * Valley replay server, which replays real runtime responses for
 * cedar-valley@1.0.0:
 *   - root.PingAsync()                            : health check + version info
 *   - client.Scheme.ProcessAsync(marking)         : the author's call, fix paths and notices included
 *   - client.Scheme.ValidateAsync(marking)        : the reader's call, valid as settled and as written
 *   - client.Scheme.SuggestAsync(assertions)      : the options a picker may offer
 *   - client.Scheme.AggregateAsync([...markings]) : rollup of multiple markings
 *   - client.Guide.ScanAsync(text)                : scan free text for guide rule matches
 *
 * Start the replay server (Node.js 22 or 24, for the server only):
 *   npx @ezclassify/conformance-server --scenario cedar-valley --port 4010
 *
 * Run:
 *   cd examples/server-tour && dotnet run
 *
 * To switch to your runtime, set the variables below. The calls stay the same,
 * but swap in markings and text your Context recognizes, because the replay
 * server only knows the inputs it recorded.
 *
 * Environment variables:
 *   EZC_API_URL         : base URL (default: http://127.0.0.1:4010)
 *   EZC_CONTEXT_ID      : Context ID to query (default: cedar-valley)
 *   EZC_CONTEXT_VERSION : Context version (default: 1.0.0)
 *   EZC_ACCESS_TOKEN    : caller identity token, if your runtime requires one
 *   EZC_CONTEXT_TOKEN   : signed Context token, if your runtime requires one
 */

using EzClassify;

var apiUrl = Environment.GetEnvironmentVariable("EZC_API_URL") ?? "http://127.0.0.1:4010";
var contextId = Environment.GetEnvironmentVariable("EZC_CONTEXT_ID") ?? "cedar-valley";
var contextVersion = Environment.GetEnvironmentVariable("EZC_CONTEXT_VERSION") ?? "1.0.0";
// The replay server asks for no credentials. A runtime usually does.
var credentials = new RestClientCredentials(
    BlankToNull(Environment.GetEnvironmentVariable("EZC_ACCESS_TOKEN")),
    BlankToNull(Environment.GetEnvironmentVariable("EZC_CONTEXT_TOKEN")));

Banner("EzClassify .NET Server Tour: EzClassifyClient");
Console.WriteLine($"  API URL  : {apiUrl}");
Console.WriteLine($"  Context ID: {contextId}@{contextVersion}");

using var root = new EzClassifyClient(apiUrl, credentials);

static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

// Centers the title inside a closed box-drawing frame so the right border
// always lines up, whatever the title length.
static void Banner(string title)
{
    const int Width = 60;
    var left = Math.Max(0, (Width - title.Length) / 2);
    var right = Math.Max(0, Width - left - title.Length);
    Console.WriteLine();
    Console.WriteLine("╔" + new string('═', Width) + "╗");
    Console.WriteLine("║" + new string(' ', left) + title + new string(' ', right) + "║");
    Console.WriteLine("╚" + new string('═', Width) + "╝");
}

static void Header(string title)
{
    Console.WriteLine();
    Console.WriteLine(new string('─', 62));
    Console.WriteLine($"  {title}");
    Console.WriteLine(new string('─', 62));
}

static void Row(string label, object? value)
{
    var str = value?.ToString() ?? "(null)";
    if (str.Length > 70) str = str[..67] + "...";
    Console.WriteLine($"  {label,-22} {str}");
}

// ── 1. PingAsync() ────────────────────────────────────────────────────────────
Header("1. root.PingAsync()");
try
{
    var ping = await root.PingAsync();
    Row("Engine version:", ping.EngineVersion);
    Row("Git tag:", ping.GitTag.Length > 0 ? ping.GitTag : "(none)");
    Row("Engine SHA:", ping.EngineSha.Length > 0 ? ping.EngineSha[..Math.Min(12, ping.EngineSha.Length)] + "…" : "(none)");
}
catch (EzClassifyClientException ex)
{
    Console.WriteLine($"  [skipped: {ex.Message}]");
    Console.WriteLine("  Start the replay server or a runtime and re-run to exercise the remaining calls.");
    Banner("No server: build OK");
    Console.WriteLine();
    return;
}

using var client = await EzClassifyClient.ConnectAsync(apiUrl, contextId, contextVersion, credentials);

// ── 2. ProcessAsync() ─────────────────────────────────────────────────────────
Header("2. client.Scheme.ProcessAsync(\"CONFIDENTIAL\")");
var result = (await client.Scheme!.ProcessAsync("CONFIDENTIAL")).Result;
Row("Rendered:", result.Rendered);
Row("Valid:", result.Valid);
Console.WriteLine("  Assertions:");
foreach (var a in result.Assertions)
    Console.WriteLine($"    • {a.ConceptId,-12} (category: {a.CategoryId,-16} confidence: {a.Confidence})");
if (result.Errors.Count > 0)
{
    Console.WriteLine("  Validation errors:");
    foreach (var e in result.Errors)
        Console.WriteLine($"    ✗ [{e.ConstraintId}] {e.Message}");
}
// Notices ride along on the process result. No second call to collect them.
Console.WriteLine($"  {result.Notices.Count} notice(s):");
foreach (var n in result.Notices)
    Console.WriteLine($"    • [{n.Type}] {n.ConceptName}{(n.Required ? " (acknowledgment required)" : "")}: {n.Text}");

// ── 3. ProcessAsync(): unrecognized marking ──────────────────────────────────
Header("3. client.Scheme.ProcessAsync(\"UNRECOGNIZED\")");
var result3 = (await client.Scheme!.ProcessAsync("UNRECOGNIZED")).Result;
Row("Rendered:", result3.Rendered);
Row("Valid:", result3.Valid);
if (result3.Errors.Count > 0)
    foreach (var e in result3.Errors)
        Console.WriteLine($"  ✗ {e.Message}");

// ── 4. ValidateAsync() ────────────────────────────────────────────────────────
Header("4. client.Scheme.ValidateAsync(\"INTERNAL\")");
var validation = (await client.Scheme!.ValidateAsync("INTERNAL")).Result;
Row("Valid:", validation.Valid);
Row("Valid as written:", validation.InputWasValid);
if (validation.Errors.Count == 0)
    Console.WriteLine("  No constraint errors; marking is valid.");
else
    foreach (var e in validation.Errors)
        Console.WriteLine($"  ✗ [{e.ConstraintId}] {e.Message}");

// ── 5. SuggestAsync() ────────────────────────────────────────────────────────
Header("5. client.Scheme.SuggestAsync([]): picker options");
var suggestResult = (await client.Scheme!.SuggestAsync(Array.Empty<Assertion>(), outputFormat: "long")).Result;
var suggestions = suggestResult.Suggestions;
Console.WriteLine($"  {suggestions.Count} suggestion(s):");
foreach (var s in suggestions.Take(6))
{
    var check = s.Selected ? "✓" : "○";
    Console.WriteLine($"  {check} {s.Label,-22} - {s.Name,-24} [{s.ConceptId}]");
}

// ── 6. AggregateAsync() ──────────────────────────────────────────────────────
Header("6. client.Scheme.AggregateAsync([two district markings])");
var agg = (await client.Scheme!.AggregateAsync(new[]
{
    "INTERNAL//OAKWOOD/PINECREST//STUDENT TRANSPORTATION SERVICES//DO NOT FORWARD",
    "CONFIDENTIAL//FINANCIAL INFORMATION//OAKWOOD/PINECREST//REDACT BEFORE RELEASE",
})).Result;
Row("Rendered:", agg.Rendered);
Console.WriteLine($"  {agg.Assertions.Count} assertion(s) in rollup:");
foreach (var a in agg.Assertions)
    Console.WriteLine($"    • {a.ConceptId} ({a.CategoryId})");
Console.WriteLine($"  {agg.Notices.Count} notice(s) on the rollup:");
foreach (var n in agg.Notices)
    Console.WriteLine($"    • [{n.Type}] {n.ConceptName}: {n.Text}");

// ── 7. Guide.ScanAsync() ─────────────────────────────────────────────────────
Header("7. client.Guide.ScanAsync(text): guide scanner");
var scanText = "Oakwood supplier bid. Share with Student Transportation Services and redact before release.";
Console.WriteLine($"  Input: {scanText}");
var scan = (await client.Guide!.ScanAsync(scanText)).Result;
Console.WriteLine($"  {scan.Count} rule match(es):");
foreach (var m in scan)
    Console.WriteLine($"    {m.Rule.Name,-32} matched {string.Join(", ", m.MatchedKeywords)}");

Banner("Tour Complete");
Console.WriteLine();
