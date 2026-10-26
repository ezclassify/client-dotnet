using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Threading;
using System.Reflection;
using System.Net.Http.Headers;
using Xunit;

namespace EzClassify.Tests;

/// <summary>
/// Every recorded exchange in the vendored wire corpus that this client's
/// surface can reach, driven through the public client against a transport
/// that answers nothing else.
///
/// The transport is the request assertion: a call that sends a different path,
/// method or body finds no recording and fails beside the one it missed. The
/// drivers are the response assertion: each parses the recorded answer through
/// the same public call an integrator makes, and checks what it read against
/// what was recorded. Nothing here restates the contract, so the corpus and the
/// client cannot agree with each other by sharing a mistake.
///
/// A request no public call can form goes through the private transport, and
/// each such driver names the reviewed reason. Every recording is driven, with
/// no skips.
/// </summary>
public class WireCorpusReplayTests
{
    private const string Version = "1.0.0";
    private const string Clearance = """{"clearance":"OPEN"}""";
    private const string Releasable = "OPEN\nThis document is releasable.";

    // Fixed fixture credentials make missing, wrong, or extra headers fail.
    private static readonly RestClientCredentials Carrier = new(null, "fixture-context-carrier");
    private static readonly RestClientCredentials Bearer = new("fixture-refused-bearer", "fixture-context-carrier");
    private static readonly RestClientCredentials Anonymous = RestClientCredentials.None;

    private static readonly Assertion Open = new("level", "OPEN", Confidence.Certain, "explicit");
    private static readonly Assertion Sensitive = new("level", "SENSITIVE", Confidence.Certain, "explicit");

    /// <summary>
    /// One replay: the Context to bind (null for the root client), the cases it
    /// must emit, each exactly once, the call that emits them, and what to check
    /// on a successful answer against the recorded body of the first case. A
    /// driver that sends through the private transport names the reason no
    /// public call can send its case.
    /// </summary>
    private sealed record Driver(
        string? Context,
        IReadOnlyList<string> Cases,
        RestClientCredentials Credentials,
        Func<EzClassifyClient, Task<object?>> Run,
        Action<object?, JsonNode?>? Verify,
        string? Seam = null,
        int Requests = 1)
    {
        public bool Private => Seam is not null;
    }

    private static Driver Root(
        string caseId,
        Func<EzClassifyClient, Task<object?>> run,
        Action<object?, JsonNode?>? verify = null,
        RestClientCredentials? credentials = null) =>
        new(null, new[] { caseId }, credentials ?? Carrier, run, verify);

    private static Driver Ctx(
        string context,
        string caseId,
        Func<EzClassifyClient, Task<object?>> run,
        Action<object?, JsonNode?>? verify = null,
        RestClientCredentials? credentials = null) =>
        new(context, new[] { caseId }, credentials ?? Carrier, run, verify);

    // A call that reads the Context description repeats the request the binding
    // probe already sent, so the one recording answers both.
    private static Driver Described(
        string context,
        string caseId,
        Func<EzClassifyClient, Task<object?>> run,
        Action<object?, JsonNode?> verify) =>
        new(context, new[] { caseId }, Carrier, run, verify, Requests: 2);

    private static Action<object?, JsonNode?> Check<T>(Action<T, JsonNode> check) =>
        (value, recorded) =>
        {
            Assert.NotNull(recorded);
            check(Assert.IsAssignableFrom<T>(value), recorded!);
        };

    // A marking result is read through to the verdicts and the marking itself,
    // which is what every reader of it acts on.
    private static void SameMarking(JsonNode recorded, bool valid, bool inputWasValid, string rendered, int assertions)
    {
        Assert.Equal(recorded["valid"]!.GetValue<bool>(), valid);
        Assert.Equal(recorded["inputWasValid"]!.GetValue<bool>(), inputWasValid);
        Assert.Equal(recorded["rendered"]!.GetValue<string>(), rendered);
        Assert.Equal(recorded["assertions"]!.AsArray().Count, assertions);
    }

    private static void SameCount<T>(IReadOnlyCollection<T> parsed, JsonNode recorded) =>
        Assert.Equal(recorded.AsArray().Count, parsed.Count);

    private static readonly IReadOnlyDictionary<string, Driver> Drivers = new Dictionary<string, Driver>(StringComparer.Ordinal)
    {
        // -- the root client -------------------------------------------------
        ["health.ok"] = Root("health.ok",
            async c => await c.PingAsync(),
            Check<PingResult>((ping, body) => Assert.Equal(body["engineVersion"]!.GetValue<string>(), ping.EngineVersion)),
            Anonymous),
        ["denial.authenticated-health-still-open"] = Root("denial.authenticated-health-still-open",
            async c => await c.PingAsync(),
            Check<PingResult>((ping, body) => Assert.Equal(body["engineVersion"]!.GetValue<string>(), ping.EngineVersion)),
            Anonymous),
        ["ctx-list.ok"] = Root("ctx-list.ok",
            async c => await c.ListContextsAsync(),
            Check<IReadOnlyList<string>>((ids, body) =>
                Assert.Equal(body["contexts"]!.AsArray().Select(id => id!.GetValue<string>()), ids))),
        ["ctx-versions.ok"] = Root("ctx-versions.ok",
            async c => await c.ListContextVersionsAsync("ez-full"),
            Check<IReadOnlyList<string>>((versions, body) =>
                Assert.Equal(body["versions"]!.AsArray().Select(v => v!.GetValue<string>()), versions))),

        // -- binding: ConnectAsync probes the Context it binds to -------------
        ["ctx-describe.ok"] = Ctx("ez-full", "ctx-describe.ok",
            c => Task.FromResult<object?>(c.ResolvedVersion),
            (version, _) => Assert.Equal(WireCorpus.Vendored.ById["ctx-describe.ok"].ResponseHeaders["x-ezclassify-resolved-version"], version)),
        ["ctx-describe.presets"] = Ctx("ez-presets", "ctx-describe.presets",
            c => Task.FromResult<object?>(c.ResolvedVersion),
            (version, _) => Assert.Equal(WireCorpus.Vendored.ById["ctx-describe.presets"].ResponseHeaders["x-ezclassify-resolved-version"], version)),

        ["ctx-describe.crosswalks"] = Described("ez-crosswalk", "ctx-describe.crosswalks",
            async c => await c.DescribeContextAsync(),
            Check<ContextDescription>((description, body) =>
            {
                Assert.Equal(body["id"]!.GetValue<string>(), description.Id);
                Assert.Equal(body["version"]!.GetValue<string>(), description.Version);
                Assert.Equal(body["scheme"]!["resolvedVersion"]!.GetValue<string>(), description.Scheme.ResolvedVersion);
                // The crosswalk is named as declared (latest) and as resolved.
                var crosswalk = Assert.Single(description.Crosswalks);
                Assert.Equal(body["crosswalks"]![0]!["id"]!.GetValue<string>(), crosswalk.Id);
                Assert.Equal("latest", crosswalk.Version);
                Assert.Equal("1.0.0", crosswalk.ResolvedVersion);
                Assert.Empty(crosswalk.Layers);
                Assert.Empty(description.Guides);
                Assert.Null(description.Access);
                Assert.Equal(
                    body["schemes"]!.AsArray().Select(s => s!["selector"]!.GetValue<string>()),
                    description.Schemes.Select(s => s.Selector));
                Assert.Equal(
                    new[] { ContextSchemeRole.Primary, ContextSchemeRole.CrosswalkSource },
                    description.Schemes[0].Roles);
                Assert.Equal(new[] { ContextSchemeRole.CrosswalkTarget }, description.Schemes[1].Roles);
            })),

        // -- documents --------------------------------------------------------
        ["ctx-document-audit.ok"] = Ctx("ez-full", "ctx-document-audit.ok",
            async c => await c.Document!.AuditAsync(Releasable, null, DocumentResponseProfile.Summary),
            Check<ContextDocumentAuditResult>((audit, body) => SameCount(audit.Result.Parts, body["result"]!["parts"]!))),
        ["ctx-document-audit.empty-text"] = Ctx("ez-full", "ctx-document-audit.empty-text",
            async c => await c.Document!.AuditAsync("")),
        ["ctx-document-assess-access.ok"] = Ctx("ez-full", "ctx-document-assess-access.ok",
            async c => await c.Document!.AssessAccessAsync(Releasable, Clearance, responseProfile: DocumentResponseProfile.Standard)),

        // -- scheme resources -------------------------------------------------
        ["ctx-scheme-summary.ok"] = Ctx("ez-presets", "ctx-scheme-summary.ok",
            async c => await c.Scheme!.SummaryAsync(),
            Check<SchemeSummary>((summary, body) =>
            {
                Assert.Equal(body["id"]!.GetValue<string>(), summary.Id);
                Assert.Equal(body["defaultRenderFormat"]?.GetValue<string>(), summary.DefaultRenderFormat);
            })),
        ["ctx-scheme-concepts.ok"] = Ctx("ez-presets", "ctx-scheme-concepts.ok",
            async c => await c.Scheme!.ConceptsAsync(),
            Check<IReadOnlyList<ConceptView>>((concepts, body) => SameCount(concepts, body))),
        ["ctx-scheme-categories.ok"] = Ctx("ez-presets", "ctx-scheme-categories.ok",
            async c => await c.Scheme!.CategoriesAsync(),
            Check<IReadOnlyList<CategoryView>>((categories, body) => SameCount(categories, body))),
        ["ctx-scheme-formats.ok"] = Ctx("ez-presets", "ctx-scheme-formats.ok",
            async c => await c.Scheme!.FormatsAsync(),
            Check<IReadOnlyList<FormatView>>((formats, body) =>
                Assert.Equal(body.AsArray().Select(f => f!["id"]!.GetValue<string>()), formats.Select(f => f.Id)))),
        ["ctx-scheme-grammar.ok"] = Ctx("ez-presets", "ctx-scheme-grammar.ok",
            async c => await c.Scheme!.GrammarAsync()),
        ["ctx-scheme-scopes.ok"] = Ctx("ez-presets", "ctx-scheme-scopes.ok",
            async c => await c.Scheme!.ScopesAsync(),
            Check<IReadOnlyList<SchemeScope>>((scopes, body) =>
                Assert.Equal(body.AsArray().Select(s => s!["id"]!.GetValue<string>()), scopes.Select(s => s.Id)))),
        ["ctx-scheme-label-presets.ok"] = Ctx("ez-presets", "ctx-scheme-label-presets.ok",
            async c => await c.Scheme!.LabelPresetsAsync(),
            Check<IReadOnlyList<LabelPresetView>>((presets, body) => SameCount(presets, body))),
        ["ctx-scheme-summary.layered"] = Ctx("ez-layered", "ctx-scheme-summary.layered",
            async c => await c.Scheme!.SummaryAsync(),
            Check<SchemeSummary>((summary, body) => Assert.Equal(body["id"]!.GetValue<string>(), summary.Id))),
        ["ctx-scheme-concepts.layered"] = Ctx("ez-layered", "ctx-scheme-concepts.layered",
            async c => await c.Scheme!.ConceptsAsync(),
            Check<IReadOnlyList<ConceptView>>((concepts, body) => SameCount(concepts, body))),
        ["ctx-scheme-categories.layered"] = Ctx("ez-layered", "ctx-scheme-categories.layered",
            async c => await c.Scheme!.CategoriesAsync(),
            Check<IReadOnlyList<CategoryView>>((categories, body) => SameCount(categories, body))),
        ["ctx-scheme-scopes.none"] = Ctx("ez-minimal", "ctx-scheme-scopes.none",
            async c => await c.Scheme!.ScopesAsync(),
            Check<IReadOnlyList<SchemeScope>>((scopes, body) =>
            {
                Assert.Empty(body.AsArray());
                Assert.Empty(scopes);
            })),
        ["ctx-scheme-summary.full"] = Ctx("ez-full", "ctx-scheme-summary.full",
            async c => await c.Scheme!.SummaryAsync(),
            Check<SchemeSummary>((summary, body) =>
            {
                Assert.Equal(body["id"]!.GetValue<string>(), summary.Id);
                Assert.Equal(body["defaultRenderFormat"]?.GetValue<string>(), summary.DefaultRenderFormat);
            })),
        ["ctx-scheme-concepts.full"] = Ctx("ez-full", "ctx-scheme-concepts.full",
            async c => await c.Scheme!.ConceptsAsync(),
            Check<IReadOnlyList<ConceptView>>((concepts, body) => SameCount(concepts, body))),
        ["ctx-scheme-categories.full"] = Ctx("ez-full", "ctx-scheme-categories.full",
            async c => await c.Scheme!.CategoriesAsync(),
            Check<IReadOnlyList<CategoryView>>((categories, body) => SameCount(categories, body))),
        ["ctx-scheme-formats.full"] = Ctx("ez-full", "ctx-scheme-formats.full",
            async c => await c.Scheme!.FormatsAsync(),
            Check<IReadOnlyList<FormatView>>((formats, body) =>
                Assert.Equal(body.AsArray().Select(f => f!["id"]!.GetValue<string>()), formats.Select(f => f.Id)))),
        ["ctx-scheme-grammar.full"] = Ctx("ez-full", "ctx-scheme-grammar.full",
            async c => await c.Scheme!.GrammarAsync()),
        ["ctx-scheme-scopes.full"] = Ctx("ez-full", "ctx-scheme-scopes.full",
            async c => await c.Scheme!.ScopesAsync(),
            Check<IReadOnlyList<SchemeScope>>((scopes, body) =>
                Assert.Equal(body.AsArray().Select(s => s!["id"]!.GetValue<string>()), scopes.Select(s => s.Id)))),
        ["ctx-scheme-label-presets.full"] = Ctx("ez-full", "ctx-scheme-label-presets.full",
            async c => await c.Scheme!.LabelPresetsAsync(),
            Check<IReadOnlyList<LabelPresetView>>((presets, body) => SameCount(presets, body))),
        ["ctx-scheme-describe-concept.ok"] = Ctx("ez-full", "ctx-scheme-describe-concept.ok",
            async c => await c.Scheme!.DescribeConceptAsync("OPEN", "level")),

        // -- reading and writing a marking -------------------------------------
        ["ctx-scheme-process.ok"] = Ctx("ez-full", "ctx-scheme-process.ok",
            async c => await c.Scheme!.ProcessAsync("OPEN"),
            Check<ContextSchemeProcessResult>((r, body) =>
                SameMarking(body["result"]!, r.Result.Valid, r.Result.InputWasValid, r.Result.Rendered, r.Result.Assertions.Count))),
        ["ctx-scheme-process.empty"] = Ctx("ez-full", "ctx-scheme-process.empty",
            async c => await c.Scheme!.ProcessAsync("", outputFormat: "default"),
            Check<ContextSchemeProcessResult>((r, body) =>
                SameMarking(body["result"]!, r.Result.Valid, r.Result.InputWasValid, r.Result.Rendered, r.Result.Assertions.Count))),
        ["ctx-scheme-process.outputs-and-options"] = Ctx("ez-full", "ctx-scheme-process.outputs-and-options",
            async c => await c.Scheme!.ProcessAsync("OPEN", outputs: new[] { "default" }, suggestFor: new[] { "level" }),
            Check<ContextSchemeProcessResult>((r, body) =>
            {
                SameMarking(body["result"]!, r.Result.Valid, r.Result.InputWasValid, r.Result.Rendered, r.Result.Assertions.Count);
                Assert.Equal(body["result"]!["renders"]!["default"]!["rendered"]!.GetValue<string>(), r.Result.Renders["default"].Rendered);
                Assert.Equal(
                    body["result"]!["suggestionsFor"]!["level"]!["suggestions"]!.AsArray().Select(s => s!["conceptId"]!.GetValue<string>()),
                    r.Result.SuggestionsFor!["level"].Suggestions.Select(s => s.ConceptId));
            })),
        ["ctx-scheme-validate.assertions"] = Ctx("ez-full", "ctx-scheme-validate.assertions",
            async c => await c.Scheme!.ValidateAsync(new[] { Open }),
            Check<ContextSchemeValidateResult>((r, body) =>
            {
                SameMarking(body["result"]!, r.Result.Valid, r.Result.InputWasValid, r.Result.Rendered, r.Result.Assertions.Count);
                Assert.Equal(body["result"]!["recognition"] is null, r.Result.Recognition is null);
            })),
        ["ctx-scheme-explain.ok"] = Ctx("ez-full", "ctx-scheme-explain.ok",
            async c => await c.Scheme!.ExplainAsync("OPEN"),
            Check<ContextSchemeExplainResult>((r, body) =>
            {
                Assert.Equal(body["result"]!["rendered"]!.GetValue<string>(), r.Result.Rendered);
                Assert.Equal(body["result"]!["entries"]!.AsArray().Count, r.Result.Entries.Count);
            })),
        ["ctx-scheme-suggest-for-categories.ok"] = Ctx("ez-full", "ctx-scheme-suggest-for-categories.ok",
            async c => await c.Scheme!.SuggestForCategoriesAsync(new[] { Open }, new[] { "level" }),
            Check<ContextSchemeSuggestForCategoriesResult>((r, body) =>
                Assert.Equal(
                    body["result"]!["level"]!["suggestions"]!.AsArray().Select(s => s!["conceptId"]!.GetValue<string>()),
                    r.Result["level"].Suggestions.Select(s => s.ConceptId)))),
        ["ctx-scheme-suggest-for-categories.empty-baseline"] = Ctx("ez-full", "ctx-scheme-suggest-for-categories.empty-baseline",
            async c => await c.Scheme!.SuggestForCategoriesAsync(Array.Empty<Assertion>(), new[] { "level" })),
        ["ctx-scheme-validate.ok"] = Ctx("ez-full", "ctx-scheme-validate.ok",
            async c => await c.Scheme!.ValidateAsync("OPEN"),
            Check<ContextSchemeValidateResult>((r, body) =>
                SameMarking(body["result"]!, r.Result.Valid, r.Result.InputWasValid, r.Result.Rendered, r.Result.Assertions.Count))),
        ["ctx-scheme-apply-preset.ok"] = Ctx("ez-presets", "ctx-scheme-apply-preset.ok",
            async c => await c.Scheme!.ApplyPresetAsync("sensitive-preset"),
            Check<ContextSchemeValidateResult>((r, body) =>
                SameMarking(body["result"]!, r.Result.Valid, r.Result.InputWasValid, r.Result.Rendered, r.Result.Assertions.Count))),
        ["ctx-scheme-apply-preset.unknown"] = Ctx("ez-presets", "ctx-scheme-apply-preset.unknown",
            async c => await c.Scheme!.ApplyPresetAsync("no-such-preset")),
        ["ctx-scheme-scan.ok"] = Ctx("ez-full", "ctx-scheme-scan.ok",
            async c => await c.Scheme!.ScanAsync("OPEN document for review"),
            Check<ContextSchemeScanResult>((r, body) =>
            {
                SameCount(r.Result.Results, body["result"]!["results"]!);
                Assert.Equal(body["result"]!["limits"]!["status"]!.GetValue<string>(), r.Result.Limits.Status.ToString());
            })),
        ["ctx-scheme-aggregate.ok"] = Ctx("ez-full", "ctx-scheme-aggregate.ok",
            async c => await c.Scheme!.AggregateAsync(new[] { "OPEN", "SENSITIVE" }),
            Check<ContextSchemeAggregateResult>((r, body) =>
            {
                Assert.Equal(body["result"]!["valid"]!.GetValue<bool>(), r.Result.Valid);
                Assert.Equal(body["result"]!["rendered"]!.GetValue<string>(), r.Result.Rendered);
            })),
        ["ctx-scheme-aggregate.assertion-sets"] = Ctx("ez-full", "ctx-scheme-aggregate.assertion-sets",
            async c => await c.Scheme!.AggregateAssertionsAsync(new[] { new[] { Open }, new[] { Sensitive } }),
            Check<ContextSchemeAggregateResult>((r, body) =>
            {
                Assert.Equal(body["result"]!["rendered"]!.GetValue<string>(), r.Result.Rendered);
                Assert.Equal(body["result"]!["assertions"]![0]!["conceptId"]!.GetValue<string>(), Assert.Single(r.Result.Assertions).ConceptId);
            })),
        ["ctx-scheme-normalize.ok"] = Ctx("ez-full", "ctx-scheme-normalize.ok",
            async c => await c.Scheme!.NormalizeAsync("open")),
        ["ctx-scheme-render.ok"] = Ctx("ez-full", "ctx-scheme-render.ok",
            async c => await c.Scheme!.RenderAsync(new[] { Open })),
        ["ctx-scheme-render.empty"] = Ctx("ez-full", "ctx-scheme-render.empty",
            async c => await c.Scheme!.RenderAsync(Array.Empty<Assertion>(), outputFormat: "default")),
        ["ctx-scheme-diff.ok"] = Ctx("ez-full", "ctx-scheme-diff.ok",
            async c => await c.Scheme!.DiffAsync("OPEN", "SENSITIVE")),
        ["ctx-scheme-view.ok"] = Ctx("ez-full", "ctx-scheme-view.ok",
            async c => await c.Scheme!.ViewAsync(),
            Check<ContextSchemeViewResult>((view, body) => Assert.Equal(
                body["result"]!["categories"]![0]!["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()),
                view.Result.Categories[0].Items.Select(item => item.Id)))),
        ["ctx-scheme-notices.ok"] = Ctx("ez-full", "ctx-scheme-notices.ok",
            async c => await c.Scheme!.NoticesAsync(new[] { Open })),
        ["ctx-scheme-filter-concepts.ok"] = Ctx("ez-full", "ctx-scheme-filter-concepts.ok",
            async c => await c.Scheme!.FilterConceptsAsync("open")),
        ["ctx-scheme-filter-concepts.no-match"] = Ctx("ez-full", "ctx-scheme-filter-concepts.no-match",
            async c => await c.Scheme!.FilterConceptsAsync("zzznomatch")),
        ["ctx-scheme-suggest.ok"] = Ctx("ez-full", "ctx-scheme-suggest.ok",
            async c => await c.Scheme!.SuggestAsync(""),
            Check<ContextSchemeSuggestResult>((r, body) =>
                Assert.Equal(
                    body["result"]!["suggestions"]!.AsArray().Select(s => s!["conceptId"]!.GetValue<string>()),
                    r.Result.Suggestions.Select(s => s.ConceptId)))),
        ["ctx-scheme-suggest.gated-hidden"] = Ctx("ez-gated", "ctx-scheme-suggest.gated-hidden",
            async c => await c.Scheme!.SuggestAsync("", filter: "export")),
        ["ctx-scheme-suggest.gated-included"] = Ctx("ez-gated", "ctx-scheme-suggest.gated-included",
            async c => await c.Scheme!.SuggestAsync("", filter: "export", includeGated: true)),
        ["ctx-scheme-check-coverage.ok"] = Ctx("ez-full", "ctx-scheme-check-coverage.ok",
            async c => await c.Scheme!.CheckCoverageAsync(new[] { "OPEN" }, "SENSITIVE")),
        ["ctx-scheme-check-coverage.excess"] = Ctx("ez-share", "ctx-scheme-check-coverage.excess",
            async c => await c.Scheme!.CheckCoverageAsync(new[] { "OPEN//ALPHA/BRAVO", "OPEN//BRAVO/CHARLIE" }, "OPEN//ALPHA/BRAVO/CHARLIE")),
        ["ctx-scheme-check-coverage.pipeline"] = Ctx("ez-release", "ctx-scheme-check-coverage.pipeline",
            async c => await c.Scheme!.CheckCoverageAsync(new[] { "OPEN//SHARED ALPHA, BRAVO" }, "OPEN//SHARED ALPHA, CHARLIE")),
        ["ctx-scheme-check-coverage.pipeline-narrowed"] = Ctx("ez-controls", "ctx-scheme-check-coverage.pipeline-narrowed",
            async c => await c.Scheme!.CheckCoverageAsync(new[] { "OPEN//VIEW ALPHA, BRAVO" }, "OPEN//SHARED ALPHA, BRAVO//VIEW ALPHA, BRAVO")),

        // -- access -------------------------------------------------------------
        ["ctx-access-check.marking"] = Ctx("ez-full", "ctx-access-check.marking",
            async c => await c.Access!.CheckByMarkingAsync("OPEN", Clearance),
            Check<ContextAccessDecisionResult>((r, body) => Assert.Equal(body["result"]!["granted"]!.GetValue<bool>(), r.Result.Granted))),
        ["ctx-access-check.assertions"] = Ctx("ez-full", "ctx-access-check.assertions",
            async c => await c.Access!.CheckAsync(new[] { Open }, Clearance),
            Check<ContextAccessDecisionResult>((r, body) => Assert.Equal(body["result"]!["granted"]!.GetValue<bool>(), r.Result.Granted))),
        ["ctx-access-check.two-attributes"] = Ctx("ez-full", "ctx-access-check.two-attributes",
            async c => await c.Access!.CheckByMarkingAsync("OPEN", """{"clearance":"OPEN","organization":"records"}"""),
            Check<ContextAccessDecisionResult>((r, body) => Assert.Equal(body["result"]!["granted"]!.GetValue<bool>(), r.Result.Granted))),
        ["ctx-access-describe-rule.ok"] = Ctx("ez-full", "ctx-access-describe-rule.ok",
            async c => await c.Access!.DescribeRuleAsync("level-rule")),
        ["ctx-access-describe-requirements.ok"] = Ctx("ez-full", "ctx-access-describe-requirements.ok",
            async c => await c.Access!.DescribeRequirementsAsync("OPEN")),
        ["ctx-access-rules.ok"] = Ctx("ez-full", "ctx-access-rules.ok",
            async c => await c.Access!.RulesAsync()),
        ["ctx-access-rules.no-policy"] = Ctx("ez-minimal", "ctx-access-rules.no-policy",
            async c => await c.Access!.RulesAsync()),

        // -- guides -------------------------------------------------------------
        ["ctx-guide-scan.ok"] = Ctx("ez-full", "ctx-guide-scan.ok",
            async c => await c.Guide!.ScanAsync("This paragraph is confidential.")),
        ["ctx-guide-evaluate.ok"] = Ctx("ez-full", "ctx-guide-evaluate.ok",
            async c => await c.Guide!.EvaluateAsync("ez-test", new[] { "rule-sensitive" })),
        ["ctx-guide-evaluate.all-guides"] = Ctx("ez-full", "ctx-guide-evaluate.all-guides",
            async c => await c.Guide!.EvaluateManyAsync(new[] { "rule-sensitive" })),
        ["ctx-guide-describe-rule.ok"] = Ctx("ez-full", "ctx-guide-describe-rule.ok",
            async c => await c.Guide!.DescribeRuleAsync("rule-sensitive")),
        ["ctx-guide-describe-rule.unknown-guide"] = Ctx("ez-full", "ctx-guide-describe-rule.unknown-guide",
            async c => await c.Guide!.DescribeRuleAsync("rule-sensitive", "no-such-guide")),
        ["ctx-guide-view.by-id"] = Ctx("ez-full", "ctx-guide-view.by-id",
            async c => await c.Guide!.ViewAsync("ez-test")),
        ["ctx-guide-view.ok"] = Ctx("ez-full", "ctx-guide-view.ok",
            async c => await c.Guide!.ViewAsync(),
            Check<ContextGuideViewResult>((view, body) =>
            {
                var recorded = body["result"]!["uncategorized"]![0]!;
                var rule = Assert.Single(view.Result.Uncategorized);
                Assert.Equal((recorded["id"]!.GetValue<string>(), recorded["outcomeText"]!.GetValue<string>()), (rule.Id, rule.OutcomeText));
            })),
        ["ctx-guide-filter-rules.ok"] = Ctx("ez-full", "ctx-guide-filter-rules.ok",
            async c => await c.Guide!.FilterRulesAsync("sensitive")),

        // -- crosswalks ---------------------------------------------------------
        ["ctx-crosswalk-translate.marking"] = Ctx("ez-full", "ctx-crosswalk-translate.marking",
            async c => await c.Crosswalk!.TranslateAsync("ez-test-to-ez-gated", "OPEN")),
        ["ctx-crosswalk-translate.translated"] = Ctx("ez-crosswalk", "ctx-crosswalk-translate.translated",
            async c => await c.Crosswalk!.TranslateAsync("ez-test-to-ez-gated", "OPEN"),
            Check<ContextCrosswalkTranslationResult>((r, body) =>
            {
                Assert.Equal("complete", r.Result.GetProperty("status").GetString());
                Assert.Equal(body["result"]!["target"]!["rendered"]!.GetValue<string>(), r.Result.GetProperty("target").GetProperty("rendered").GetString());
            })),
        ["ctx-crosswalk-translate.forbidden"] = Ctx("ez-crosswalk", "ctx-crosswalk-translate.forbidden",
            async c => await c.Crosswalk!.TranslateAsync("ez-test-to-ez-gated", "SENSITIVE"),
            Check<ContextCrosswalkTranslationResult>((r, body) =>
            {
                Assert.Equal("forbidden", r.Result.GetProperty("status").GetString());
                Assert.False(r.Result.TryGetProperty("target", out _));
                Assert.Equal(body["result"]!["forbiddenBy"]![0]!["mappingId"]!.GetValue<string>(), r.Result.GetProperty("forbiddenBy")[0].GetProperty("mappingId").GetString());
            })),
        ["ctx-crosswalk-translate.assertions"] = Ctx("ez-full", "ctx-crosswalk-translate.assertions",
            async c => await c.Crosswalk!.TranslateAsync("ez-test-to-ez-gated", new[] { Open })),

        ["ctx-scheme-process.undeclared-format"] = Ctx("ez-full", "ctx-scheme-process.undeclared-format",
            async c => await c.Scheme!.ProcessAsync("OPEN", outputFormat: "nope")),
        ["ctx-scheme-suggest.facts"] = Ctx("ez-full", "ctx-scheme-suggest.facts",
            async c => await c.Scheme!.SuggestAsync(new[] { Open })),
        ["ctx-scheme-describe-concept.unknown-concept"] = Ctx("ez-full", "ctx-scheme-describe-concept.unknown-concept",
            async c => await c.Scheme!.DescribeConceptAsync("NOPE", "level")),

        // -- refusals -------------------------------------------------------------
        ["denial.authenticated-no-credential"] = Ctx("ez-full", "denial.authenticated-no-credential",
            async c => await c.Scheme!.ProcessAsync("OPEN"), credentials: Anonymous),
        ["denial.authenticated-bad-credential"] = Ctx("ez-full", "denial.authenticated-bad-credential",
            async c => await c.Scheme!.ProcessAsync("OPEN"), credentials: Bearer),
        ["denial.context-without-carrier"] = Ctx("ez-full", "denial.context-without-carrier",
            async c => await c.Scheme!.ProcessAsync("OPEN"), credentials: Anonymous),
        ["denial.ctx-scheme-without-carrier"] = Ctx("ez-full", "denial.ctx-scheme-without-carrier",
            async c => await c.Scheme!.ConceptsAsync(), credentials: Anonymous),
    };

    // The reviewed reasons a recorded case is sent through the private transport.
    private const string NoReadyCall = "getReady has no public call; SpecOperationCoverageTests records why.";
    private const string UnformableRequest = "The public request types cannot form this body: both or neither source, an input format beside structured facts, an undeclared field, or a missing input.";
    private const string UndeclaredQuery = "The public calls never send a query parameter their operation does not declare.";
    private const string NoSuchRoute = "The route or method is one the contract does not serve, so no public call sends it.";

    // The finite malformed-request and absent-route seams, each with its reason.
    private static readonly IReadOnlyDictionary<string, Driver> PrivateDrivers = new Dictionary<string, Driver>(StringComparer.Ordinal)
    {
        ["ready.ok"] = Raw("ready.ok", NoReadyCall, Anonymous),
        ["ctx-scheme-validate.both-sources"] = Raw("ctx-scheme-validate.both-sources", UnformableRequest),
        ["ctx-scheme-validate.no-source"] = Raw("ctx-scheme-validate.no-source", UnformableRequest),
        ["ctx-crosswalk-translate.both-sources"] = Raw("ctx-crosswalk-translate.both-sources", UnformableRequest),
        ["ctx-crosswalk-translate.no-source"] = Raw("ctx-crosswalk-translate.no-source", UnformableRequest),
        ["route-miss.route-not-found"] = Raw("route-miss.route-not-found", NoSuchRoute, Anonymous),
        ["route-miss.ctx-operation-not-found"] = Raw("route-miss.ctx-operation-not-found", NoSuchRoute),
        ["method-guard.allow-header"] = Raw("method-guard.allow-header", NoSuchRoute),
        ["ctx-scheme-aggregate.input-format-with-sets"] = Raw("ctx-scheme-aggregate.input-format-with-sets", UnformableRequest),
        ["ctx-scheme-suggest.both-sources"] = Raw("ctx-scheme-suggest.both-sources", UnformableRequest),
        ["ctx-scheme-suggest.no-source"] = Raw("ctx-scheme-suggest.no-source", UnformableRequest),
        ["ctx-scheme-suggest.input-format-with-facts"] = Raw("ctx-scheme-suggest.input-format-with-facts", UnformableRequest),
        ["ctx-scheme-process.undeclared-request-field-fix"] = Raw("ctx-scheme-process.undeclared-request-field-fix", UnformableRequest),
        ["ctx-scheme-process.undeclared-request-field-preserveDeprecated"] = Raw("ctx-scheme-process.undeclared-request-field-preserveDeprecated", UnformableRequest),
        ["ctx-scheme-process.undeclared-request-field-normalization"] = Raw("ctx-scheme-process.undeclared-request-field-normalization", UnformableRequest),
        ["ctx-scheme-process.undeclared-request-field-skipRepair"] = Raw("ctx-scheme-process.undeclared-request-field-skipRepair", UnformableRequest),
        ["ctx-scheme-process.undeclared-request-field-skipInference"] = Raw("ctx-scheme-process.undeclared-request-field-skipInference", UnformableRequest),
        ["ctx-scheme-process.undeclared-request-field-tolerant"] = Raw("ctx-scheme-process.undeclared-request-field-tolerant", UnformableRequest),
        ["ctx-scheme-process.undeclared-request-field-validateOnly"] = Raw("ctx-scheme-process.undeclared-request-field-validateOnly", UnformableRequest),
        ["ctx-scheme-process.undeclared-request-field-scope"] = Raw("ctx-scheme-process.undeclared-request-field-scope", UnformableRequest),
        ["ctx-scheme-process.undeclared-request-field-format"] = Raw("ctx-scheme-process.undeclared-request-field-format", UnformableRequest),
        ["ctx-scheme-process.undeclared-request-field-repairStrategy"] = Raw("ctx-scheme-process.undeclared-request-field-repairStrategy", UnformableRequest),
        ["ctx-scheme-process.undeclared-request-field-unknownField"] = Raw("ctx-scheme-process.undeclared-request-field-unknownField", UnformableRequest),
        ["ctx-scheme-validate.undeclared-field"] = Raw("ctx-scheme-validate.undeclared-field", UnformableRequest),
        ["ctx-scheme-explain.undeclared-field"] = Raw("ctx-scheme-explain.undeclared-field", UnformableRequest),
        ["ctx-scheme-normalize.undeclared-field"] = Raw("ctx-scheme-normalize.undeclared-field", UnformableRequest),
        ["ctx-scheme-aggregate.undeclared-field"] = Raw("ctx-scheme-aggregate.undeclared-field", UnformableRequest),
        ["ctx-scheme-suggest.undeclared-field"] = Raw("ctx-scheme-suggest.undeclared-field", UnformableRequest),
        ["ctx-scheme-suggest-for-categories.undeclared-field"] = Raw("ctx-scheme-suggest-for-categories.undeclared-field", UnformableRequest),
        ["ctx-scheme-render.undeclared-field"] = Raw("ctx-scheme-render.undeclared-field", UnformableRequest),
        ["ctx-scheme-scan.undeclared-field"] = Raw("ctx-scheme-scan.undeclared-field", UnformableRequest),
        ["ctx-document-audit.undeclared-guide-field"] = Raw("ctx-document-audit.undeclared-guide-field", UnformableRequest),
        ["ctx-scheme-process.missing-input"] = Raw("ctx-scheme-process.missing-input", UnformableRequest),
        ["ctx-scheme-label-presets.undeclared-query-parameter"] = Raw("ctx-scheme-label-presets.undeclared-query-parameter", UndeclaredQuery),
        ["ctx-scheme-process.undeclared-query-parameter"] = Raw("ctx-scheme-process.undeclared-query-parameter", UndeclaredQuery),
        ["health.undeclared-query-parameter"] = Raw("health.undeclared-query-parameter", UndeclaredQuery, Anonymous),
    };

    private static Driver Raw(string id, string seam, RestClientCredentials? credentials = null) =>
        new(null, new[] { id }, credentials ?? Carrier, async client =>
        {
            var recorded = WireCorpus.Vendored.ById[id];
            var send = typeof(EzClassifyClient).GetMethod("SendCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(send);
            object? body = recorded.HasRequestBody ? recorded.RequestBody : null;
            if (recorded.HasRequestBody && recorded.RequestBody is null) body = JsonSerializer.SerializeToElement<object?>(null);
            var response = await Assert.IsAssignableFrom<Task<string>>(send.Invoke(client, new object?[]
            {
                new HttpMethod(recorded.Method), recorded.Target.TrimStart('/'), body, CancellationToken.None,
            }));
            return JsonNode.Parse(response);
        }, null, Seam: seam);

    private static IReadOnlyDictionary<string, Driver> AllDrivers => Drivers.Concat(PrivateDrivers).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    /// <summary>Every driver, by name.</summary>
    public static TheoryData<string> DriverNames()
    {
        var names = new TheoryData<string>();
        foreach (var name in AllDrivers.Keys) names.Add(name);
        return names;
    }

    [Fact]
    public void EveryRecordedCaseIsDrivenOrSkipped()
    {
        var claimed = new HashSet<string>(AllDrivers.Values.SelectMany(d => d.Cases), StringComparer.Ordinal);
        var unaccounted = WireCorpus.Vendored.Cases.Select(c => c.Id).Where(id => !claimed.Contains(id)).ToList();
        Assert.True(unaccounted.Count == 0,
            "These recorded exchanges have no maintained driver: " + string.Join(", ", unaccounted));
    }

    [Fact]
    public void DriversAndSkipsNameOnlyRecordedCases()
    {
        var unknown = AllDrivers.Values.SelectMany(d => d.Cases)
            .Where(id => !WireCorpus.Vendored.ById.ContainsKey(id)).ToList();
        Assert.True(unknown.Count == 0, "No recorded case has these ids: " + string.Join(", ", unknown));
    }

    [Fact]
    public void EachCaseIsClaimedOnce()
    {
        var duplicated = AllDrivers.Values.SelectMany(d => d.Cases)
            .GroupBy(id => id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicated.Count == 0, "Claimed more than once: " + string.Join(", ", duplicated));
    }

    [Theory]
    [MemberData(nameof(DriverNames))]
    public async Task RecordedExchangeReplaysThroughThePublicClient(string name)
    {
        await ReplayAsync(name, socket: false);
    }

    [Theory]
    [MemberData(nameof(DriverNames))]
    public async Task RecordedExchangeReplaysOverActualHTTP(string name)
    {
        await ReplayAsync(name, socket: true);
    }

    private static async Task<(WireReplayHandler Handler, WireObservationHandler Observer, object? Value, Exception? Error)> ExecuteAsync(
        Driver driver, WireCorpus corpus, bool socket)
    {
        var handler = new WireReplayHandler(
            corpus, driver.Cases, driver.Context is null ? null : (driver.Context, Version), driver.Requests);
        await using var server = socket ? WireLoopbackServer.Start(handler) : null;
        var observer = new WireObservationHandler(socket ? new HttpClientHandler() : handler);
        using var http = new HttpClient(observer) { BaseAddress = new Uri(server?.Url ?? "http://replay.invalid/") };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        object? value = null;
        Exception? error = null;
        try
        {
            using var client = driver.Context is null
                ? new EzClassifyClient(http, driver.Credentials)
                : await EzClassifyClient.ConnectAsync(http, driver.Context, Version, driver.Credentials);
            value = await driver.Run(client);
        }
        catch (Exception caught)
        {
            error = caught;
        }
        return (handler, observer, value, error);
    }

    /// <summary>The drivers whose recorded answer is a success the public call decodes, by name.</summary>
    internal static IReadOnlyList<string> SuccessDriverNames() =>
        Drivers.Where(d => !d.Value.Private && WireCorpus.Vendored.ById[d.Value.Cases[0]].Status is >= 200 and < 300)
            .Select(d => d.Key).ToList();

    /// <summary>Every recorded case a public call sends, as the replays here prove.</summary>
    internal static IEnumerable<WireCase> PublicCases() =>
        Drivers.Values.Where(d => !d.Private).SelectMany(d => d.Cases).Select(id => WireCorpus.Vendored.ById[id]);

    /// <summary>The recorded case a driver replays.</summary>
    internal static WireCase CaseOf(string name) => WireCorpus.Vendored.ById[Drivers[name].Cases[0]];

    /// <summary>Run the named driver's public call against <paramref name="corpus"/> and return what it threw.</summary>
    internal static async Task<Exception?> RunAgainstAsync(string name, WireCorpus corpus)
    {
        var (_, observer, _, error) = await ExecuteAsync(Drivers[name], corpus, socket: false);
        observer.Dispose();
        return error;
    }

    private static async Task ReplayAsync(string name, bool socket)
    {
        var driver = AllDrivers[name];
        var (handler, observer, value, error) = await ExecuteAsync(driver, WireCorpus.Vendored, socket);
        using var _ = observer;

        Assert.True(handler.Misses.Count == 0,
            $"{name}: the client sent a request no expected case recorded:\n{string.Join("\n", handler.Misses)}");
        Assert.True(handler.HeaderProblems.Count == 0, $"{name}: {string.Join("\n", handler.HeaderProblems)}");
        Assert.True(handler.Unemitted.Count == 0,
            $"{name}: the client never sent the request recorded by {string.Join(", ", handler.Unemitted)}");

        var recorded = WireCorpus.Vendored.ById[driver.Cases[0]];
        var received = observer.Responses.Last();
        VerifyReceived(recorded, received);

        if (recorded.Status >= 400)
        {
            var refusal = Assert.IsType<EzClassifyClientException>(error);
            Assert.Equal(recorded.Status, refusal.StatusCode);
            if (recorded.ResponseBody?["code"] is JsonValue code)
                Assert.Equal(code.GetValue<string>(), refusal.Code);
            Assert.Equal(recorded.ResponseBody?["detail"]?.GetValue<string>(), refusal.Detail);
            Assert.Equal(Replayer.StableJson(recorded.ResponseBody), Replayer.StableJson(JsonNode.Parse(Assert.IsType<string>(refusal.ResponseBody))));
            return;
        }

        Assert.True(error is null, $"{name}: the client could not read the recorded answer: {error}");
        if (value?.GetType().GetProperty("Resolution")?.GetValue(value) is ContextResolution resolution)
            Assert.Equal(recorded.ResponseBody!["resolution"]!["contextId"]!.GetValue<string>(), resolution.ContextId);
        driver.Verify?.Invoke(value, recorded.ResponseBody);
        if (!driver.Private) AssertNothingSharedIsMutable(name, value);
    }

    // A result may be handed to several callers, so a caller changing its copy
    // must not be able to change anyone else's. Mutating every reachable
    // collection through the interfaces it is typed as must leave the result
    // exactly as it was decoded.
    private static void AssertNothingSharedIsMutable(string name, object? value)
    {
        if (value is null or string || value.GetType().IsPrimitive) return;
        var before = CachePlanMutator.Snapshot(value);
        CachePlanMutator.Mutate(value);
        Assert.True(before == CachePlanMutator.Snapshot(value), $"{name}: a collection in the decoded result accepted a change");
    }
    internal static void VerifyReceived(WireCase recorded, WireObservedResponse received)
    {
        Assert.Equal(recorded.Status, received.Status);
        Assert.Equal(Replayer.StableJson(recorded.ResponseBody), Replayer.StableJson(JsonNode.Parse(received.Body)));
        foreach (var (header, expected) in recorded.ResponseHeaders)
            Assert.Equal(expected, received.Headers[header]);
    }

}
