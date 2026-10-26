using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace EzClassify;

/// <summary>Static JSON parsing helpers for EzClassify API result types.</summary>
/// <remarks>
/// Every parser throws <see cref="JsonException"/> for a body that lacks a member
/// the contract requires, at any depth, and names the member in
/// <see cref="JsonException.Path"/>, such as <c>$.assessment.emitted</c>. A member
/// the contract marks optional may be absent or null.
/// </remarks>
public static class ResultParser
{
    /// <summary>Parse an assertion object from a JSON element.</summary>
    public static Assertion ParseAssertion(JsonElement a)
    {
        ResponseCheck.Require("Assertion", a);
        return ReadAssertion(a);
    }

    internal static Assertion ReadAssertion(JsonElement a) => new(
        CategoryId: JsonRead.RequiredString(a, "categoryId"),
        ConceptId: JsonRead.RequiredString(a, "conceptId"),
        Confidence: new Confidence(JsonRead.RequiredString(a, "confidence")),
        Source: JsonRead.RequiredString(a, "source"),
        ExpiresAt: JsonRead.OptionalString(a, "expiresAt"),
        Metadata: OptionalOpaque(a, "metadata"),
        Provenance: a.TryGetProperty("provenance", out var provenance) && provenance.ValueKind == JsonValueKind.Object
            ? ContextResultParser.ReadSourceReference(provenance) : null,
        ReplacedConceptId: JsonRead.OptionalString(a, "replacedConceptId"),
        SourceRule: JsonRead.OptionalString(a, "sourceRule"),
        Value: JsonRead.OptionalString(a, "value")
    );

    /// <summary>Parse a validation error from a JSON element.</summary>
    public static ValidationError ParseValidationError(JsonElement e)
    {
        ResponseCheck.Require("ValidationError", e);
        return ReadValidationError(e);
    }

    internal static ValidationError ReadValidationError(JsonElement e) => new(
        ConstraintId: JsonRead.RequiredString(e, "constraintId"),
        Message: JsonRead.RequiredString(e, "message"),
        Severity: WireToken.ReadOptional<Severity>(JsonRead.OptionalString(e, "severity")),
        AffectedConcepts: JsonRead.TolerantOptionalStringList(e, "affectedConcepts"),
        CategoryId: JsonRead.OptionalString(e, "categoryId"),
        DidYouMean: JsonRead.TolerantOptionalStringList(e, "didYouMean"),
        SuggestedFix: JsonRead.OptionalString(e, "suggestedFix")
    );

    /// <summary>Parse a warning from a JSON element.</summary>
    public static Warning ParseWarning(JsonElement w)
    {
        ResponseCheck.Require("Warning", w);
        return ReadWarning(w);
    }

    internal static Warning ReadWarning(JsonElement w) => new(
        Kind: new WarningKind(JsonRead.RequiredString(w, "kind")),
        Message: JsonRead.RequiredString(w, "message"),
        ConceptId: JsonRead.OptionalString(w, "conceptId"),
        CategoryId: JsonRead.OptionalString(w, "categoryId"),
        RuleId: JsonRead.OptionalString(w, "ruleId"),
        Replacement: JsonRead.OptionalString(w, "replacement")
    );

    /// <summary>Parse a concept reference (conceptId + categoryId) from a JSON element.</summary>
    public static ConceptRef ParseConceptRef(JsonElement el)
    {
        ResponseCheck.Require("FixConceptReference", el);
        return ReadConceptRef(el);
    }

    internal static ConceptRef ReadConceptRef(JsonElement el) => new(
        ConceptId: JsonRead.RequiredString(el, "conceptId"),
        CategoryId: JsonRead.RequiredString(el, "categoryId")
    );

    /// <summary>
    /// Parse a proposed fix from a JSON element: one way to make the marking
    /// valid, carried only in <c>proposedFixes</c>.
    /// </summary>
    public static Fix ParseFix(JsonElement el)
    {
        ResponseCheck.Require("Fix", el);
        return ReadFix(el);
    }

    internal static Fix ReadFix(JsonElement el) => new(
        Message: JsonRead.RequiredString(el, "message"),
        Rendered: JsonRead.RequiredString(el, "rendered"),
        Keeps: JsonRead.RequiredList(el, "keeps", ReadConceptRef),
        Adds: JsonRead.RequiredList(el, "adds", ReadConceptRef),
        Removes: JsonRead.RequiredList(el, "removes", ReadConceptRef)
    );

    /// <summary>
    /// Parse a change the engine made from a JSON element, carried only in
    /// <c>appliedFixes</c>. <c>action</c> is required and must be a string; a
    /// token the <see cref="FixAction"/> type does not declare is kept as an
    /// unknown value rather than refused.
    /// </summary>
    public static Change ParseChange(JsonElement el)
    {
        ResponseCheck.Require("Change", el);
        return ReadChange(el);
    }

    internal static Change ReadChange(JsonElement el) => new(
        RuleId: JsonRead.RequiredString(el, "ruleId"),
        Action: new FixAction(JsonRead.RequiredString(el, "action")),
        Message: JsonRead.RequiredString(el, "message"),
        Add: OptionalConceptRefList(el, "add"),
        Remove: OptionalConceptRefList(el, "remove"),
        TriggerConceptIds: JsonRead.TolerantOptionalStringList(el, "triggerConceptIds")
    );

    /// <summary>
    /// Read an optional array of concept references under the tolerant
    /// contract: a missing or non-array property yields null.
    /// </summary>
    private static IReadOnlyList<ConceptRef>? OptionalConceptRefList(JsonElement el, string key) =>
        JsonRead.TolerantOptionalList(el, key, ReadConceptRef);

    /// <summary>Parse a trace entry (one explanation-trace step) from a JSON element.</summary>
    public static TraceEntry ParseTraceEntry(JsonElement el)
    {
        ResponseCheck.Require("TraceEntry", el);
        return ReadTraceEntry(el);
    }

    internal static TraceEntry ReadTraceEntry(JsonElement el) => new(
        Stage: JsonRead.RequiredString(el, "stage"),
        Action: JsonRead.RequiredString(el, "action"),
        Detail: JsonRead.RequiredString(el, "detail"),
        ConceptId: JsonRead.OptionalString(el, "conceptId"),
        RuleId: JsonRead.OptionalString(el, "ruleId"),
        TriggerConceptIds: JsonRead.TolerantOptionalStringList(el, "triggerConceptIds"),
        ConceptName: JsonRead.OptionalString(el, "conceptName"),
        CategoryId: JsonRead.OptionalString(el, "categoryId")
    );

    /// <summary>Parse a trace repair (a normalization the engine applied) from a JSON element.</summary>
    public static TraceRepair ParseTraceRepair(JsonElement el)
    {
        ResponseCheck.Require("TraceRepair", el);
        return ReadTraceRepair(el);
    }

    internal static TraceRepair ReadTraceRepair(JsonElement el) => new(
        RuleId: JsonRead.RequiredString(el, "ruleId"),
        Description: JsonRead.RequiredString(el, "description"),
        Confidence: JsonRead.RequiredString(el, "confidence"),
        Before: JsonRead.RequiredString(el, "before"),
        After: JsonRead.RequiredString(el, "after"),
        Cosmetic: el.TryGetProperty("cosmetic", out var cos)
            && cos.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? cos.GetBoolean() : (bool?)null
    );

    /// <summary>Parse a trace inference (a concept the engine derived) from a JSON element.</summary>
    public static TraceInference ParseTraceInference(JsonElement el)
    {
        ResponseCheck.Require("TraceInference", el);
        return ReadTraceInference(el);
    }

    internal static TraceInference ReadTraceInference(JsonElement el) => new(
        RuleId: JsonRead.RequiredString(el, "ruleId"),
        ConceptId: JsonRead.RequiredString(el, "conceptId"),
        Explanation: JsonRead.RequiredString(el, "explanation"),
        ConceptName: JsonRead.OptionalString(el, "conceptName")
    );

    /// <summary>Parse a trimmed trace validation issue (constraintId + message) from a JSON element.</summary>
    public static TraceValidationIssue ParseTraceValidationIssue(JsonElement el)
    {
        ResponseCheck.Require("TraceValidationIssue", el);
        return ReadTraceValidationIssue(el);
    }

    internal static TraceValidationIssue ReadTraceValidationIssue(JsonElement el) => new(
        ConstraintId: JsonRead.RequiredString(el, "constraintId"),
        Message: JsonRead.RequiredString(el, "message")
    );

    /// <summary>
    /// Parse a full explanation trace from a JSON element. Every list field is
    /// constructed from its wire array.
    /// </summary>
    public static ExplanationTrace ParseExplanationTrace(JsonElement el)
    {
        ResponseCheck.Require("ExplanationTrace", el);
        return ReadExplanationTrace(el);
    }

    internal static ExplanationTrace ReadExplanationTrace(JsonElement el) => new(
        Input: JsonRead.RequiredString(el, "input"),
        SchemeId: JsonRead.RequiredString(el, "schemeId"),
        Rendered: JsonRead.RequiredString(el, "rendered"),
        Entries: JsonRead.RequiredList(el, "entries", ReadTraceEntry),
        Repairs: JsonRead.RequiredList(el, "repairs", ReadTraceRepair),
        Inferences: JsonRead.RequiredList(el, "inferences", ReadTraceInference),
        ValidationErrors: JsonRead.RequiredList(el, "validationErrors", ReadTraceValidationIssue),
        FinalAssertions: JsonRead.RequiredList(el, "finalAssertions", ReadAssertion)
    );

    /// <summary>
    /// Parse a guide breakdown entry from a JSON element. <c>outcome</c> stays
    /// opaque JSON, as it does on a guide rule.
    /// </summary>
    public static GuideBreakdownEntry ParseGuideBreakdownEntry(JsonElement el)
    {
        ResponseCheck.Require("GuideBreakdownEntry", el);
        return ReadGuideBreakdownEntry(el);
    }

    internal static GuideBreakdownEntry ReadGuideBreakdownEntry(JsonElement el) => new(
        Rule: ContextResultParser.ReadRuleReference(JsonRead.RequiredProperty(el, "rule")),
        Outcome: JsonRead.RequiredProperty(el, "outcome").Clone(),
        IsComputed: JsonRead.RequiredBool(el, "isComputed"),
        WasOverridden: JsonRead.RequiredBool(el, "wasOverridden"),
        OverriddenBy: el.TryGetProperty("overriddenBy", out var overriddenBy) && overriddenBy.ValueKind != JsonValueKind.Null
            ? ContextResultParser.ReadRuleReference(overriddenBy)
            : null
    );

    private static ConceptModernization ReadConceptModernization(JsonElement el)
    {
        JsonRead.RequireObject(el, "concept modernization");
        return new ConceptModernization(
            ConceptId: JsonRead.RequiredString(el, "conceptId"),
            ReplacedConceptId: JsonRead.RequiredString(el, "replacedConceptId"),
            ConceptDisplay: JsonRead.RequiredString(el, "conceptDisplay"),
            ReplacedConceptDisplay: JsonRead.RequiredString(el, "replacedConceptDisplay"));
    }

    /// <summary>Parse what validate answers from a JSON string.</summary>
    public static ValidateResult ParseValidateResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        ResponseCheck.Require("ValidateResult", doc.RootElement);
        return ParseValidateResultElement(doc.RootElement);
    }

    // Element core: a scan region, a preset expansion and the validate envelope
    // all carry this shape nested, and read it from the element already in hand.
    internal static ValidateResult ParseValidateResultElement(JsonElement root)
    {
        JsonRead.RequireObject(root, "validate result");
        return new(
            AppliedFixes: JsonRead.RequiredList(root, "appliedFixes", ReadChange),
            Assertions: JsonRead.RequiredList(root, "assertions", ReadAssertion),
            Assessment: ParseRenderAssessmentElement(JsonRead.RequiredProperty(root, "assessment")),
            Errors: JsonRead.RequiredList(root, "errors", ReadValidationError),
            InputWasValid: JsonRead.RequiredBool(root, "inputWasValid"),
            Notices: JsonRead.RequiredList(root, "notices", ContextResultParser.ParseNoticeElement),
            Rendered: JsonRead.RequiredString(root, "rendered"),
            Valid: JsonRead.RequiredBool(root, "valid"),
            Warnings: JsonRead.RequiredList(root, "warnings", ReadWarning),
            DisplayColor: OptionalDisplayColor(root),
            Recognition: Present(root, "recognition")
                ? ParseRecognitionResult(root.GetProperty("recognition"))
                : null);
    }

    /// <summary>Parse what process answers from a JSON string.</summary>
    public static ProcessResult ParseProcessResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        ResponseCheck.Require("ProcessResult", doc.RootElement);
        return ParseProcessResultElement(doc.RootElement);
    }

    // Element core so a nested process result can be read from the element
    // already in hand instead of being re-serialized and re-parsed.
    internal static ProcessResult ParseProcessResultElement(JsonElement root)
    {
        JsonRead.RequireObject(root, "process result");
        return new(
            AppliedFixes: JsonRead.RequiredList(root, "appliedFixes", ReadChange),
            Assertions: JsonRead.RequiredList(root, "assertions", ReadAssertion),
            Assessment: ParseRenderAssessmentElement(JsonRead.RequiredProperty(root, "assessment")),
            Errors: JsonRead.RequiredList(root, "errors", ReadValidationError),
            InputWasValid: JsonRead.RequiredBool(root, "inputWasValid"),
            Notices: JsonRead.RequiredList(root, "notices", ContextResultParser.ParseNoticeElement),
            ProposedFixes: JsonRead.RequiredList(root, "proposedFixes", ReadFix),
            Rendered: JsonRead.RequiredString(root, "rendered"),
            Renders: ParseRenderResults(root),
            Valid: JsonRead.RequiredBool(root, "valid"),
            Warnings: JsonRead.RequiredList(root, "warnings", ReadWarning),
            // Sent only when repair changed the marking. A fix for a rule repair enforced is
            // applied to this reading, not to Assertions, so dropping it breaks that fix.
            AssertionsBeforeRepair: JsonRead.TolerantOptionalList(root, "assertionsBeforeRepair", ReadAssertion),
            DisplayColor: OptionalDisplayColor(root),
            Recognition: Present(root, "recognition")
                ? ParseRecognitionWithCorrections(root.GetProperty("recognition"))
                : null,
            // Absent unless the request named categories in suggestFor. Absent and
            // empty are different answers, so absent stays null.
            SuggestionsFor: Present(root, "suggestionsFor")
                ? ContextResultParser.ParseSuggestResultsByCategoryElement(root.GetProperty("suggestionsFor"))
                : null);
    }

    // A presentation default the scheme derives for the marking. Optional on every result that
    // carries it, so absent and null both read as "no color".
    private static ConceptColor? OptionalDisplayColor(JsonElement root) =>
        root.TryGetProperty("displayColor", out var color) && color.ValueKind != JsonValueKind.Null
            ? ContextResultParser.ParseConceptColorElement(color)
            : null;

    private static bool Present(JsonElement parent, string key) =>
        parent.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null;

    // The reader's form of recognition: how far it got, what the text was read
    // as and in which role, and what it could not account for.
    internal static RecognitionResult ParseRecognitionResult(JsonElement root)
    {
        JsonRead.RequireObject(root, "recognition");
        return new(
            Status: RecognitionStatusOf(root),
            Unmatched: JsonRead.RequiredList(root, "unmatched", ParseInputSpan),
            InputFormat: JsonRead.OptionalString(root, "inputFormat"),
            Role: RecognitionRoleOf(root),
            Surface: JsonRead.OptionalString(root, "surface"));
    }

    // The author's form adds the corrections that turn the text into canonical text.
    private static RecognitionWithCorrections ParseRecognitionWithCorrections(JsonElement root)
    {
        JsonRead.RequireObject(root, "recognition");
        return new(
            Corrections: JsonRead.RequiredList(root, "corrections", ParseRecognitionCorrection),
            Status: RecognitionStatusOf(root),
            Unmatched: JsonRead.RequiredList(root, "unmatched", ParseInputSpan),
            InputFormat: JsonRead.OptionalString(root, "inputFormat"),
            Role: RecognitionRoleOf(root),
            Surface: JsonRead.OptionalString(root, "surface"));
    }

    private static RecognitionStatus RecognitionStatusOf(JsonElement root) => new(JsonRead.RequiredString(root, "status"));

    // Present only for a scheme that declares surfaces, and only when the role is
    // decided. A token this SDK does not declare is kept, not refused.
    private static StructuralRole? RecognitionRoleOf(JsonElement root) =>
        WireToken.ReadOptional<StructuralRole>(JsonRead.OptionalString(root, "role"));

    private static RecognitionCorrection ParseRecognitionCorrection(JsonElement root) => new(
        End: JsonRead.RequiredInt(root, "end"),
        Original: JsonRead.RequiredString(root, "original"),
        Reason: new RecognitionCorrectionReason(JsonRead.RequiredString(root, "reason")),
        Replacement: JsonRead.RequiredString(root, "replacement"),
        Start: JsonRead.RequiredInt(root, "start"));

    private static InputSpan ParseInputSpan(JsonElement root) => new(
        End: JsonRead.RequiredInt(root, "end"),
        Start: JsonRead.RequiredInt(root, "start"),
        Text: JsonRead.RequiredString(root, "text"));

    private static IReadOnlyDictionary<string, RenderResult> ParseRenderResults(JsonElement root)
    {
        var renders = JsonRead.RequiredProperty(root, "renders");
        if (renders.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected renders object");
        var parsed = new Dictionary<string, RenderResult>();
        foreach (var render in renders.EnumerateObject())
        {
            var value = render.Value;
            parsed[render.Name] = new RenderResult(
                JsonRead.RequiredString(value, "rendered"),
                ParseRenderAssessmentElement(JsonRead.RequiredProperty(value, "assessment")));
        }
        return new ReadOnlyDictionary<string, RenderResult>(parsed);
    }

    internal static RenderAssessment ParseRenderAssessmentElement(JsonElement root) => new(
        Emitted: JsonRead.RequiredList(root, "emitted", ParseAssertionRef),
        Omitted: JsonRead.RequiredList(root, "omitted", ParseAssertionRef),
        Collapsed: ParseCollapsed(root),
        Conflicts: JsonRead.RequiredList(root, "conflicts", ParseRenderFieldConflict),
        Unrepresentable: JsonRead.RequiredList(root, "unrepresentable", ParseUnrepresentableValue));

    private static AssertionRef ParseAssertionRef(JsonElement root) => new(
        ConceptId: JsonRead.RequiredString(root, "conceptId"),
        CategoryId: JsonRead.RequiredString(root, "categoryId"),
        Value: JsonRead.OptionalString(root, "value"));

    private static IReadOnlyList<IReadOnlyList<AssertionRef>> ParseCollapsed(JsonElement root) =>
        JsonRead.RequiredList(root, "collapsed", group => JsonRead.ElementList(group, ParseAssertionRef));

    private static RenderFieldConflict ParseRenderFieldConflict(JsonElement root) => new(
        Field: JsonRead.RequiredString(root, "field"),
        Assertions: JsonRead.RequiredList(root, "assertions", ParseAssertionRef));

    // A fact names the concept it could not write; an output refused whole names
    // the output instead. Only those two kinds exist, so an entry naming neither
    // is malformed rather than a kind a newer server added.
    private static UnrepresentableValue ParseUnrepresentableValue(JsonElement root)
    {
        JsonRead.RequireObject(root, "unrepresentable entry");
        var reason = JsonRead.RequiredString(root, "reason");
        var detail = JsonRead.OptionalString(root, "detail");
        if (root.TryGetProperty("conceptId", out _))
            return new UnrepresentableFact(
                ConceptId: JsonRead.RequiredString(root, "conceptId"),
                Reason: new UnrepresentableFactReason(reason),
                Detail: detail);
        if (root.TryGetProperty("output", out _))
            return new UnrepresentableOutput(
                Output: JsonRead.RequiredString(root, "output"),
                Reason: new UnrepresentableOutputReason(reason),
                Detail: detail);
        throw new JsonException("An unrepresentable entry names neither a conceptId nor an output");
    }

    /// <summary>Parse the suggestions array from a suggest-result envelope JSON string.</summary>
    public static IReadOnlyList<Suggestion> ParseSuggestions(string json)
    {
        using var doc = JsonDocument.Parse(json);
        ResponseCheck.Require("SuggestResult", doc.RootElement);
        return JsonRead.RequiredList(doc.RootElement, "suggestions", ReadSuggestion);
    }

    internal static Suggestion ReadSuggestion(JsonElement el) => new(
        CategoryId: JsonRead.RequiredString(el, "categoryId"),
        ConceptId: JsonRead.RequiredString(el, "conceptId"),
        Description: JsonRead.RequiredString(el, "description"),
        Group: JsonRead.RequiredString(el, "group"),
        Label: JsonRead.RequiredString(el, "label"),
        Name: JsonRead.RequiredString(el, "name"),
        Order: JsonRead.RequiredInt(el, "order"),
        Selected: JsonRead.RequiredBool(el, "selected"),
        Views: ContextResultParser.RequiredStringMap(el, "views"),
        Gated: el.TryGetProperty("gated", out var gated)
            && gated.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? gated.GetBoolean() : (bool?)null,
        Requires: JsonRead.TolerantOptionalStringList(el, "requires"),
        RequiresDisplay: JsonRead.TolerantOptionalStringList(el, "requiresDisplay"),
        Source: JsonRead.OptionalString(el, "source"));

    /// <summary>Parse an aggregate result from a JSON string.</summary>
    public static AggregateResult ParseAggregateResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        ResponseCheck.Require("AggregateResult", doc.RootElement);
        return ParseAggregateResultElement(doc.RootElement);
    }

    // Element core: a nested occurrence is read from the element already
    // in hand instead of being re-serialized and re-parsed.
    internal static AggregateResult ParseAggregateResultElement(JsonElement root)
    {
        return new AggregateResult(
            AppliedFixes: JsonRead.RequiredList(root, "appliedFixes", ReadChange),
            Assertions: JsonRead.RequiredList(root, "assertions", ReadAssertion),
            Errors: JsonRead.RequiredList(root, "errors", ReadValidationError),
            Notices: JsonRead.RequiredList(root, "notices", ContextResultParser.ParseNoticeElement),
            ProposedFixes: JsonRead.RequiredList(root, "proposedFixes", ReadFix),
            Rendered: JsonRead.RequiredString(root, "rendered"),
            Valid: JsonRead.RequiredBool(root, "valid"),
            Warnings: JsonRead.RequiredList(root, "warnings", ReadWarning),
            DisplayColor: OptionalDisplayColor(root)
        );
    }

    /// <summary>Parse a diff result from a JSON string.</summary>
    public static DiffResult ParseDiffResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        ResponseCheck.Require("DiffResult", doc.RootElement);
        return ParseDiffResultElement(doc.RootElement);
    }

    // Element core: a nested occurrence is read from the element already
    // in hand instead of being re-serialized and re-parsed.
    internal static DiffResult ParseDiffResultElement(JsonElement root)
    {
        return new DiffResult(
            Added: JsonRead.RequiredList(root, "added", ReadAssertion),
            Common: JsonRead.RequiredList(root, "common", ReadAssertion),
            Removed: JsonRead.RequiredList(root, "removed", ReadAssertion)
        );
    }

    /// <summary>Parse scan matches from a JSON string.</summary>
    public static IReadOnlyList<ScanMatch> ParseScanMatches(string json)
    {
        using var doc = JsonDocument.Parse(json);
        ResponseCheck.RequireEach("ScanResult", doc.RootElement);
        return ParseScanMatchesElement(doc.RootElement);
    }

    /// <summary>
    /// Read a scan's answer: the markings, and how far the scan got.
    /// </summary>
    /// <remarks>
    /// The limit report is not optional. An empty list of markings is what a
    /// clean document and an abandoned scan both look like, and a caller that
    /// cannot tell them apart cannot decide what happens to the document.
    /// </remarks>
    internal static ScanResponseBody ParseScanResponseElement(JsonElement root)
    {
        JsonRead.RequireObject(root, "scan response");
        var limits = JsonRead.RequiredProperty(root, "limits");
        JsonRead.RequireObject(limits, "scan limits");
        return new ScanResponseBody(
            Results: ParseScanMatchesElement(JsonRead.RequiredProperty(root, "results")),
            Limits: new ScanLimits(
                RegionsFound: JsonRead.RequiredInt(limits, "regionsFound"),
                Status: new ScanStatus(JsonRead.RequiredString(limits, "status")),
                LimitHit: WireToken.ReadOptional<RecognitionLimit>(JsonRead.OptionalString(limits, "limitHit")),
                RegionsCapped: limits.TryGetProperty("regionsCapped", out var rc) && rc.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? rc.GetBoolean()
                    : null));
    }

    internal static IReadOnlyList<ScanMatch> ParseScanMatchesElement(JsonElement root)
    {
        JsonRead.RequireArray(root, "scan match");
        return JsonRead.ElementList(root, el => new ScanMatch(
            End: JsonRead.RequiredInt(el, "end"),
            Marking: JsonRead.RequiredString(el, "marking"),
            Result: ParseValidateResultElement(JsonRead.RequiredProperty(el, "result")),
            Role: new StructuralRole(JsonRead.RequiredString(el, "role")),
            Start: JsonRead.RequiredInt(el, "start"),
            Type: JsonRead.RequiredString(el, "type"),
            Wrapped: JsonRead.RequiredBool(el, "wrapped"),
            Inner: JsonRead.OptionalString(el, "inner")));
    }

    /// <summary>Parse a guide rule from a JSON element.</summary>
    public static GuideRule ParseGuideRule(JsonElement el)
    {
        ResponseCheck.Require("GuideRule", el);
        return ReadGuideRule(el);
    }

    internal static GuideRule ReadGuideRule(JsonElement el)
    {
        return new GuideRule(
            Id: JsonRead.RequiredString(el, "id"),
            Name: JsonRead.RequiredString(el, "name"),
            Outcome: JsonRead.RequiredProperty(el, "outcome").Clone(),
            Category: JsonRead.OptionalString(el, "category"),
            Description: JsonRead.OptionalString(el, "description"),
            Keywords: JsonRead.StrictOptionalStringList(el, "keywords"),
            Match: OptionalOpaqueList(el, "match"),
            Overrides: JsonRead.TolerantOptionalStringList(el, "overrides"),
            Rationale: JsonRead.OptionalString(el, "rationale"),
            When: OptionalOpaque(el, "when")
        );
    }

    /// <summary>Parse an array of guide scan results from a JSON string.</summary>
    public static IReadOnlyList<GuideScanResult> ParseGuideScanResults(string json)
    {
        using var doc = JsonDocument.Parse(json);
        ResponseCheck.RequireEach("GuideScanResult", doc.RootElement);
        return ParseGuideScanResultsElement(doc.RootElement);
    }

    internal static IReadOnlyList<GuideScanResult> ParseGuideScanResultsElement(JsonElement root)
    {
        JsonRead.RequireArray(root, "guide scan result");
        return JsonRead.ElementList(root, el => new GuideScanResult(
            MatchedKeywords: JsonRead.StrictStringList(el, "matchedKeywords"),
            Rule: ReadGuideRule(JsonRead.RequiredProperty(el, "rule")),
            Spans: JsonRead.RequiredList(el, "spans", span => new GuideScanSpan(
                End: JsonRead.RequiredInt(span, "end"),
                Keyword: JsonRead.RequiredString(span, "keyword"),
                Start: JsonRead.RequiredInt(span, "start"),
                Confidence: span.TryGetProperty("confidence", out var confidence) && confidence.ValueKind == JsonValueKind.Number
                    ? confidence.GetDouble() : (double?)null)),
            GuideId: JsonRead.OptionalString(el, "guideId"),
            Provenance: el.TryGetProperty("provenance", out var provenance) && provenance.ValueKind == JsonValueKind.Object
                ? ContextResultParser.ReadSourceReference(provenance) : null));
    }

    /// <summary>Parse a single evaluation result from a JSON string.</summary>
    public static EvaluationResult ParseEvaluationResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        ResponseCheck.Require("EvaluationResult", doc.RootElement);
        return ReadEvaluationResult(doc.RootElement);
    }

    /// <summary>Parse an array of evaluation results from a JSON string.</summary>
    public static IReadOnlyList<EvaluationResult> ParseEvaluationResults(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        JsonRead.RequireArray(root, "evaluation result");
        ResponseCheck.RequireEach("EvaluationResult", root);
        return JsonRead.ElementList(root, ReadEvaluationResult);
    }

    /// <summary>Parse a single evaluation result from a JSON element.</summary>
    public static EvaluationResult ParseEvaluationResultElement(JsonElement root)
    {
        ResponseCheck.Require("EvaluationResult", root);
        return ReadEvaluationResult(root);
    }

    internal static EvaluationResult ReadEvaluationResult(JsonElement root) => new(
        ActiveComputedRules: JsonRead.RequiredList(root, "activeComputedRules", ReadGuideRule),
        AggregatedMarking: JsonRead.RequiredString(root, "aggregatedMarking"),
        Breakdown: JsonRead.RequiredList(root, "breakdown", ReadGuideBreakdownEntry),
        ContributingBreakdown: JsonRead.RequiredList(root, "contributingBreakdown", ReadGuideBreakdownEntry),
        EffectiveMarkings: JsonRead.StrictStringList(root, "effectiveMarkings"),
        SelectedRules: JsonRead.RequiredList(root, "selectedRules", ReadGuideRule),
        AggregatedMarkings: ContextResultParser.OptionalStringMap(root, "aggregatedMarkings"),
        GuideId: JsonRead.OptionalString(root, "guideId"));

    /// <summary>Parse an access decision from a JSON string.</summary>
    public static AccessDecision ParseAccessDecision(string json)
    {
        using var doc = JsonDocument.Parse(json);
        ResponseCheck.Require("AccessDecision", doc.RootElement);
        return ReadAccessDecision(doc.RootElement);
    }

    /// <summary>Parse an access decision from a nested JSON element.</summary>
    public static AccessDecision ParseAccessDecisionElement(JsonElement root)
    {
        ResponseCheck.Require("AccessDecision", root);
        return ReadAccessDecision(root);
    }

    internal static AccessDecision ReadAccessDecision(JsonElement root)
    {
        JsonRead.RequireObject(root, "access decision");
        return new AccessDecision(
            Granted: JsonRead.RequiredBool(root, "granted"),
            Rules: JsonRead.RequiredList(root, "rules", rule => new AccessRuleResult(
                Activation: ContextResultParser.ReadAccessRuleActivation(JsonRead.RequiredProperty(rule, "activation")),
                Comparisons: ContextResultParser.ReadAccessComparisonResults(JsonRead.RequiredProperty(rule, "comparisons")),
                Granted: JsonRead.RequiredBool(rule, "granted"),
                Name: JsonRead.RequiredString(rule, "name"),
                Rule: ContextResultParser.ReadRuleReference(JsonRead.RequiredProperty(rule, "rule")),
                RuleIndex: JsonRead.RequiredInt(rule, "ruleIndex"),
                Skipped: JsonRead.RequiredBool(rule, "skipped"),
                ModernizedFrom: JsonRead.TolerantOptionalList(rule, "modernizedFrom", ReadConceptModernization),
                Reason: JsonRead.OptionalString(rule, "reason"))),
            Provenance: root.TryGetProperty("provenance", out var provenance) && provenance.ValueKind == JsonValueKind.Object
                ? ContextResultParser.ReadSourceReference(provenance)
                : null
        );
    }

    /// <summary>
    /// Carry an optional opaque property through as a boxed <see cref="JsonElement"/>.
    /// Cloned so it survives disposal of the source <see cref="JsonDocument"/>.
    /// Missing or JSON-null values yield <c>null</c>.
    /// </summary>
    private static JsonElement? OptionalOpaque(JsonElement parent, string key)
        => parent.TryGetProperty(key, out var el) && el.ValueKind != JsonValueKind.Null
            ? el.Clone()
            : null;

    /// <summary>
    /// Carry an optional array of opaque values through as cloned elements.
    /// Missing, null, or non-array values yield <c>null</c>.
    /// </summary>
    private static IReadOnlyList<JsonElement>? OptionalOpaqueList(JsonElement parent, string key)
    {
        if (!parent.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<JsonElement>();
        foreach (var el in arr.EnumerateArray()) list.Add(el.Clone());
        return list.AsReadOnly();
    }

}
