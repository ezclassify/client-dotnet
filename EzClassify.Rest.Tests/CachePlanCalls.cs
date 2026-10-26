using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace EzClassify.Tests;

/// <summary>
/// The case drivers for the cache plans: for each operation and corpus case a
/// plan names, the call to this SDK's public method that sends exactly the
/// case's recorded request.
///
/// <para>Two operations differ from their names. This SDK has one decision
/// surface in two shapes: <c>Access.CheckAsync</c> takes assertions and is
/// <c>access.check</c>, and <c>Access.CheckByMarkingAsync</c> takes a marking and
/// is <c>context.checkAccess</c>.</para>
/// </summary>
internal static class CachePlanCalls
{
    private const string Clearance = """{"clearance":"OPEN"}""";
    private const string Releasable = "OPEN\nThis document is releasable.";
    private static readonly Assertion Open = new("level", "OPEN", Confidence.Certain, "explicit");
    private static readonly Assertion Sensitive = new("level", "SENSITIVE", Confidence.Certain, "explicit");

    private delegate Task<object> Call(EzClassifyClient client, CancellationToken token, bool reorder);

    private static object Of<T>(T value) where T : notnull => value;

    private static readonly IReadOnlyDictionary<string, Call> Table = new Dictionary<string, Call>(StringComparer.Ordinal)
    {
        // -- cached --------------------------------------------------------------
        ["scheme.process|ctx-scheme-process.ok"] = async (c, ct, _) => Of(await c.Scheme!.ProcessAsync("OPEN", cancellationToken: ct)),
        ["scheme.process|ctx-scheme-process.undeclared-format"] = async (c, ct, _) => Of(await c.Scheme!.ProcessAsync("OPEN", outputFormat: "nope", cancellationToken: ct)),
        ["scheme.validate|ctx-scheme-validate.ok"] = async (c, ct, _) => Of(await c.Scheme!.ValidateAsync("OPEN", cancellationToken: ct)),
        ["scheme.explain|ctx-scheme-explain.ok"] = async (c, ct, _) => Of(await c.Scheme!.ExplainAsync("OPEN", cancellationToken: ct)),
        ["scheme.normalize|ctx-scheme-normalize.ok"] = async (c, ct, _) => Of(await c.Scheme!.NormalizeAsync("open", cancellationToken: ct)),
        ["scheme.render|ctx-scheme-render.ok"] = async (c, ct, _) => Of(await c.Scheme!.RenderAsync(new[] { Open }, cancellationToken: ct)),
        ["scheme.diff|ctx-scheme-diff.ok"] = async (c, ct, _) => Of(await c.Scheme!.DiffAsync("OPEN", "SENSITIVE", cancellationToken: ct)),
        ["scheme.aggregate|ctx-scheme-aggregate.ok"] = async (c, ct, _) => Of(await c.Scheme!.AggregateAsync(new[] { "OPEN", "SENSITIVE" }, cancellationToken: ct)),
        ["scheme.aggregateAssertions|ctx-scheme-aggregate.assertion-sets"] = async (c, ct, _) =>
            Of(await c.Scheme!.AggregateAssertionsAsync(new[] { new[] { Open }, new[] { Sensitive } }, cancellationToken: ct)),
        ["scheme.applyPreset|ctx-scheme-apply-preset.ok"] = async (c, ct, _) => Of(await c.Scheme!.ApplyPresetAsync("sensitive-preset", cancellationToken: ct)),
        ["scheme.suggest|ctx-scheme-suggest.ok"] = async (c, ct, _) => Of(await c.Scheme!.SuggestAsync("", cancellationToken: ct)),
        ["scheme.suggestForCategories|ctx-scheme-suggest-for-categories.ok"] = async (c, ct, _) =>
            Of(await c.Scheme!.SuggestForCategoriesAsync(new[] { Open }, new[] { "level" }, cancellationToken: ct)),
        ["scheme.scan|ctx-scheme-scan.ok"] = async (c, ct, _) => Of(await c.Scheme!.ScanAsync("OPEN document for review", cancellationToken: ct)),
        ["scheme.describeConcept|ctx-scheme-describe-concept.ok"] = async (c, ct, _) => Of(await c.Scheme!.DescribeConceptAsync("OPEN", "level", cancellationToken: ct)),
        ["scheme.describeConcept|ctx-scheme-describe-concept.unknown-concept"] = async (c, ct, _) => Of(await c.Scheme!.DescribeConceptAsync("NOPE", "level", cancellationToken: ct)),
        ["access.describeAllAccessRules|ctx-access-rules.ok"] = async (c, ct, _) => Of(await c.Access!.RulesAsync(ct)),

        // -- decisions -----------------------------------------------------------
        ["access.check|ctx-access-check.assertions"] = async (c, ct, _) => Of(await c.Access!.CheckAsync(new[] { Open }, Clearance, cancellationToken: ct)),
        ["context.checkAccess|ctx-access-check.marking"] = async (c, ct, _) => Of(await c.Access!.CheckByMarkingAsync("OPEN", Clearance, cancellationToken: ct)),
        // The attribute map has no order on the wire, so a caller who builds it in
        // the other order still finds the entry.
        ["context.checkAccess|ctx-access-check.two-attributes"] = async (c, ct, reorder) => Of(await c.Access!.CheckByMarkingAsync(
            "OPEN",
            reorder ? """{"organization":"records","clearance":"OPEN"}""" : """{"clearance":"OPEN","organization":"records"}""",
            cancellationToken: ct)),

        // -- never cached --------------------------------------------------------
        ["context.audit|ctx-document-audit.ok"] = async (c, ct, _) => Of(await c.Document!.AuditAsync(Releasable, null, DocumentResponseProfile.Summary, ct)),
        ["context.assessDocumentAccess|ctx-document-assess-access.ok"] = async (c, ct, _) =>
            Of(await c.Document!.AssessAccessAsync(Releasable, Clearance, responseProfile: DocumentResponseProfile.Standard, cancellationToken: ct)),
        ["context.accessRequirements|ctx-access-describe-requirements.ok"] = async (c, ct, _) => Of(await c.Access!.DescribeRequirementsAsync("OPEN", ct)),
        ["context.translate|ctx-crosswalk-translate.translated"] = async (c, ct, _) => Of(await c.Crosswalk!.TranslateAsync("ez-test-to-ez-gated", "OPEN", ct)),
        ["context.translate|ctx-crosswalk-translate.forbidden"] = async (c, ct, _) => Of(await c.Crosswalk!.TranslateAsync("ez-test-to-ez-gated", "SENSITIVE", ct)),

        // -- uncached ------------------------------------------------------------
        ["scheme.buildSchemeView|ctx-scheme-view.ok"] = async (c, ct, _) => Of(await c.Scheme!.ViewAsync(ct)),
        ["scheme.filterConcepts|ctx-scheme-filter-concepts.ok"] = async (c, ct, _) => Of(await c.Scheme!.FilterConceptsAsync("open", ct)),
        ["scheme.checkCoverage|ctx-scheme-check-coverage.ok"] = async (c, ct, _) => Of(await c.Scheme!.CheckCoverageAsync(new[] { "OPEN" }, "SENSITIVE", cancellationToken: ct)),
        ["scheme.collectNotices|ctx-scheme-notices.ok"] = async (c, ct, _) => Of(await c.Scheme!.NoticesAsync(new[] { Open }, ct)),
        ["guide.scan|ctx-guide-scan.ok"] = async (c, ct, _) => Of(await c.Guide!.ScanAsync("This paragraph is confidential.", cancellationToken: ct)),
        ["guide.evaluate|ctx-guide-evaluate.ok"] = async (c, ct, _) => Of(await c.Guide!.EvaluateAsync("ez-test", new[] { "rule-sensitive" }, ct)),
        ["guide.evaluateAll|ctx-guide-evaluate.all-guides"] = async (c, ct, _) => Of(await c.Guide!.EvaluateManyAsync(new[] { "rule-sensitive" }, ct)),
        ["guide.describeRule|ctx-guide-describe-rule.ok"] = async (c, ct, _) => Of(await c.Guide!.DescribeRuleAsync("rule-sensitive", cancellationToken: ct)),
        ["guide.filterRules|ctx-guide-filter-rules.ok"] = async (c, ct, _) => Of(await c.Guide!.FilterRulesAsync("sensitive", cancellationToken: ct)),
        ["guide.buildGuideView|ctx-guide-view.ok"] = async (c, ct, _) => Of(await c.Guide!.ViewAsync(cancellationToken: ct)),
        ["access.describeAccessRule|ctx-access-describe-rule.ok"] = async (c, ct, _) => Of(await c.Access!.DescribeRuleAsync("level-rule", ct)),
        ["access.describeAccessRequirements|ctx-access-describe-requirements.ok"] = async (c, ct, _) => Of(await c.Access!.DescribeRequirementsAsync("OPEN", ct)),

        // -- scheme resources: per-call operations that reach the server every time --
        ["scheme.summary|ctx-scheme-summary.full"] = async (c, ct, _) => Of(await c.Scheme!.SummaryAsync(cancellationToken: ct)),
        ["scheme.concepts|ctx-scheme-concepts.full"] = async (c, ct, _) => Of(await c.Scheme!.ConceptsAsync(cancellationToken: ct)),
        ["scheme.categories|ctx-scheme-categories.full"] = async (c, ct, _) => Of(await c.Scheme!.CategoriesAsync(cancellationToken: ct)),
        ["scheme.formats|ctx-scheme-formats.full"] = async (c, ct, _) => Of(await c.Scheme!.FormatsAsync(cancellationToken: ct)),
        ["scheme.grammar|ctx-scheme-grammar.full"] = async (c, ct, _) => Of(await c.Scheme!.GrammarAsync(cancellationToken: ct)),
        ["scheme.scopes|ctx-scheme-scopes.full"] = async (c, ct, _) => Of(await c.Scheme!.ScopesAsync(cancellationToken: ct)),
        ["scheme.labelPresets|ctx-scheme-label-presets.full"] = async (c, ct, _) => Of(await c.Scheme!.LabelPresetsAsync(ct)),
    };

    /// <summary>Every operation and case pair this SDK drives through a public method.</summary>
    public static IEnumerable<string> Drivers => Table.Keys;

    /// <summary>
    /// Call <paramref name="operation"/> with the request <paramref name="caseId"/> records and settle
    /// with the result or the failure, which is what the plan's later steps read.
    /// </summary>
    public static async Task<CachePlanOutcome> InvokeAsync(
        EzClassifyClient client, WireCorpus corpus, string operation, string caseId, bool reorder, CancellationToken token)
    {
        try
        {
            var key = $"{operation}|{caseId}";
            if (!Table.TryGetValue(key, out var call))
                throw new InvalidOperationException($"no driver for {key}; add it to CachePlanCalls");
            return new CachePlanOutcome(operation, caseId, await call(client, token, reorder), null);
        }
        catch (Exception error)
        {
            return new CachePlanOutcome(operation, caseId, null, error);
        }
    }
}
