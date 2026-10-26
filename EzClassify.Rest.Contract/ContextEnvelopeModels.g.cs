// Code generated from the canonical OpenAPI contract (a40dcc10f4874a49ad7ef620eaee6eb51fc01a922530bd6439160731a691e6b9) by gen.mjs@5cf243baa1066cacdc9db5123130bfc7cc034a1a04ecc014f5cbca1e4f02e6ba. DO NOT EDIT.
// Re-run via: make regen
#nullable enable

using System.Collections.Generic;
using System.Text.Json;

namespace EzClassify;

/// <summary>A reference to one assertion in a rendered state.</summary>
/// <param name="ConceptId">The concept asserted.</param>
/// <param name="CategoryId">The category it was asserted in.</param>
/// <param name="Value">Its value, for a concept that carries free text.</param>
public record AssertionRef(string ConceptId, string CategoryId, string? Value = null);

/// <summary>Two assertions that claim the same render field.</summary>
/// <param name="Field">The field, by <c>name</c> or category id.</param>
/// <param name="Assertions">The assertions in conflict.</param>
public record RenderFieldConflict(string Field, IReadOnlyList<AssertionRef> Assertions);

/// <summary>
/// Something a render could not write: one fact, as an
/// <see cref="UnrepresentableFact"/>, or a whole output, as an
/// <see cref="UnrepresentableOutput"/>. Match on the type to tell them apart;
/// only a fact names a concept.
/// </summary>
public abstract record UnrepresentableValue
{
    // The contract has exactly these two kinds, so the hierarchy stays closed
    // to this assembly: a caller that handles both has handled every kind.
    private protected UnrepresentableValue() { }

    /// <summary>Human-readable detail, for a message rather than a branch.</summary>
    public abstract string? Detail { get; init; }
}

/// <summary>A fact the selected format has no way to write.</summary>
/// <param name="ConceptId">The concept that cannot be written.</param>
/// <param name="Reason">Why the format cannot write it.</param>
/// <param name="Detail">Human-readable detail, for a message rather than a branch.</param>
public sealed record UnrepresentableFact(
    string ConceptId,
    UnrepresentableFactReason Reason,
    string? Detail = null) : UnrepresentableValue;

/// <summary>
/// An output refused whole, before any of its facts was written. Only a scheme
/// that declares surfaces refuses one, because only its views choose a format
/// by condition.
/// </summary>
/// <param name="Output">The output refused, by its <c>surface/view</c> address.</param>
/// <param name="Reason">Why it was refused.</param>
/// <param name="Detail">Human-readable detail, for a message rather than a branch.</param>
public sealed record UnrepresentableOutput(
    string Output,
    UnrepresentableOutputReason Reason,
    string? Detail = null) : UnrepresentableValue;

/// <summary>Complete preservation accounting for one render.</summary>
/// <param name="Emitted">Facts this render wrote.</param>
/// <param name="Omitted">Facts this render did not write.</param>
/// <param name="Collapsed">Groups of facts that collapsed into one written equivalent.</param>
/// <param name="Conflicts">Fields that two assertions both claim.</param>
/// <param name="Unrepresentable">Facts that cannot be written in this format at all, and outputs refused whole.</param>
public record RenderAssessment(
    IReadOnlyList<AssertionRef> Emitted,
    IReadOnlyList<AssertionRef> Omitted,
    IReadOnlyList<IReadOnlyList<AssertionRef>> Collapsed,
    IReadOnlyList<RenderFieldConflict> Conflicts,
    IReadOnlyList<UnrepresentableValue> Unrepresentable);

/// <summary>A named render and its preservation assessment.</summary>
/// <param name="Rendered">The rendered marking.</param>
/// <param name="Assessment">What this render did to the state it was given.</param>
public record RenderResult(string Rendered, RenderAssessment Assessment);

/// <summary>Rendered marking payload and its preservation assessment.</summary>
/// <param name="Result">The rendered marking string.</param>
/// <param name="Assessment">What this render did to the state it was given: what was written, what was left out, what collapsed, what this format cannot write. Bound to this render, not to the format in the abstract.</param>
public record RenderResponse(string Result, RenderAssessment Assessment);

/// <summary>Normalized marking payload returned inside a Context envelope.</summary>
/// <param name="Result">Canonical normalized form of the marking string.</param>
public record NormalizeResponse(string Result);

/// <summary>Resolved Context envelope returned by scheme processing, the author's call.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeProcessResult(ContextResolution Resolution, ProcessResult Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by scheme validation, the reader's call, and by label preset expansion.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeValidateResult(ContextResolution Resolution, ValidateResult Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by a scheme explanation: the trace of how the engine settled one marking.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeExplainResult(ContextResolution Resolution, ExplanationTrace Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by scheme suggestions.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeSuggestResult(ContextResolution Resolution, SuggestResult Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>
/// Resolved Context envelope returned by per-category scheme suggestions: one
/// <see cref="SuggestResult"/> per requested category id, the same map a process
/// call returns in <see cref="ProcessResult.SuggestionsFor"/>.
/// </summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeSuggestForCategoriesResult(ContextResolution Resolution, IReadOnlyDictionary<string, SuggestResult> Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by scheme aggregation.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeAggregateResult(ContextResolution Resolution, AggregateResult Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>What a scan answers: the markings it found, and how far it got. An
/// empty list is what a clean document and an abandoned scan both look like, so
/// the limit report travels with the results rather than being inferred.</summary>
/// <param name="Results">The markings the scan found, in document order.</param>
/// <param name="Limits">How far the scan got, and what stopped it.</param>
public record ScanResponseBody(IReadOnlyList<ScanMatch> Results, ScanLimits Limits);

/// <summary>Resolved Context envelope returned by scheme scans.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeScanResult(ContextResolution Resolution, ScanResponseBody Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by scheme rendering.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeRenderResult(ContextResolution Resolution, RenderResponse Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by scheme normalization.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeNormalizeResult(ContextResolution Resolution, NormalizeResponse Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by scheme coverage checks.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeCoverageResult(ContextResolution Resolution, CoverageReport Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by concept descriptions.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeConceptDescriptionResult(ContextResolution Resolution, ConceptDescription Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by scheme diffs.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeDiffResult(ContextResolution Resolution, DiffResult Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by concept filtering.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeFilterConceptsResult(ContextResolution Resolution, FilterResult Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by scheme notice collection.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeNoticesResult(ContextResolution Resolution, IReadOnlyList<Notice> Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by scheme structural views.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextSchemeViewResult(ContextResolution Resolution, SchemeView Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by guide scans.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextGuideScanResult(ContextResolution Resolution, IReadOnlyList<GuideScanResult> Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by guide evaluation.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextGuideEvaluationResult(ContextResolution Resolution, EvaluationResult Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by guide-rule filtering.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextGuideFilterRulesResult(ContextResolution Resolution, FilterResult Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by guide-rule descriptions.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextGuideRuleDescriptionResult(ContextResolution Resolution, RuleDescription Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by guide structural views.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextGuideViewResult(ContextResolution Resolution, GuideView Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by trusted-target access checks.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextAccessDecisionResult(ContextResolution Resolution, AccessDecision Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by access requirement descriptions.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextAccessRequirementsResult(ContextResolution Resolution, AccessRequirementsDescription Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>Resolved Context envelope returned by access-rule descriptions.</summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextAccessRuleDescriptionResult(ContextResolution Resolution, AccessRuleDescription Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>
/// Resolved Context envelope returned by directional crosswalk translation.
/// The translation result is preserved as contract-owned JSON because its
/// completed and forbidden branches are a discriminated union.
/// </summary>
/// <param name="Resolution">Single resolved composition used throughout the operation.</param>
/// <param name="Result">Typed operation result.</param>
/// <param name="OperationLimitations">Limitations applying to the operation as a whole.</param>
/// <param name="ResponseProjection">Projection metadata, present only for document operations.</param>
public record ContextCrosswalkTranslationResult(ContextResolution Resolution, JsonElement Result, IReadOnlyList<AssessmentLimitation> OperationLimitations, DocumentResponseProjection? ResponseProjection = null);

/// <summary>
/// One artifact taking part in a resolved Context: the id and version the
/// Context declared, plus the concrete version those resolved to.
/// </summary>
/// <param name="Id">Component id from the Context declaration.</param>
/// <param name="Version">Version literal the Context declares: a concrete semver or <c>latest</c>.</param>
/// <param name="ResolvedVersion">Concrete version after <c>latest</c> resolution.</param>
/// <param name="Layers">Layers stacked beneath this component, in composition order. Empty when it has none.</param>
public record ContextComponentRef(
    string Id,
    string Version,
    string ResolvedVersion,
    IReadOnlyList<ContextComponentRef> Layers);

/// <summary>
/// One scheme a Context holds, and the selector that addresses it. Read it as a
/// sentence: this Context holds a scheme you select with <c>Selector</c>, it is
/// <c>Id</c> at <c>Version</c>, and it plays these <c>Roles</c>.
/// </summary>
/// <param name="Selector">Context-local key that selects this scheme. It is what <c>?scheme=</c> takes on the scheme resources, and it need not equal <c>Id</c>.</param>
/// <param name="Id">Scheme id, from the scheme document itself.</param>
/// <param name="Version">Scheme version, from the scheme document itself.</param>
/// <param name="Roles">What this scheme is to the Context. Never empty.</param>
public record ContextSchemeDescriptor(
    string Selector,
    string Id,
    string Version,
    IReadOnlyList<ContextSchemeRole> Roles);

/// <summary>
/// What a Context is made of, as the server resolved it: the composed scheme,
/// every scheme held, the guides, the access policy and the crosswalks, each
/// with the version it declares and the concrete version that answered. An
/// integrator records these component versions to say which artifacts a
/// decision was made against.
/// </summary>
/// <param name="Id">Context id.</param>
/// <param name="Version">Concrete Context version after <c>latest</c> resolution.</param>
/// <param name="Scheme">The composed scheme, the base of the layer stack. Its <c>Layers</c> are the Context's layers in composition order.</param>
/// <param name="Schemes">Every scheme the Context holds, primary first, each with the selector that addresses it.</param>
/// <param name="Guides">Guide components in precedence order. Empty when the Context declares none.</param>
/// <param name="Crosswalks">Crosswalk components in declaration order, each with the layers it stacks and the concrete version that answered. Empty when the Context declares none.</param>
/// <param name="Access">Access policy component, or null when the Context declares none.</param>
/// <param name="Description">Human-readable description copied from the Context declaration, or null.</param>
public record ContextDescription(
    string Id,
    string Version,
    ContextComponentRef Scheme,
    IReadOnlyList<ContextSchemeDescriptor> Schemes,
    IReadOnlyList<ContextComponentRef> Guides,
    IReadOnlyList<ContextComponentRef> Crosswalks,
    ContextComponentRef? Access = null,
    string? Description = null);

/// <summary>
/// The shape of every component schema a success response can carry, generated
/// from the contract's <c>required</c> lists. <see cref="ResponseCheck"/> reads it.
/// </summary>
internal static class ResponseShapes
{
    internal static readonly IReadOnlyDictionary<string, ShapeNode> Components = new Dictionary<string, ShapeNode>
    {
        ["AccessComparisonResult"] = ShapeNode.Obj(required: "left operator right matched", members: new[] { ("left", ShapeNode.Ref("AccessOperand")), ("right", ShapeNode.Ref("AccessOperand")), ("missing", ShapeNode.Arr(null)) }),
        ["AccessDecision"] = ShapeNode.Obj(required: "granted rules", members: new[] { ("rules", ShapeNode.Arr(ShapeNode.Ref("AccessRuleResult"))), ("provenance", ShapeNode.Ref("SourceReference")) }),
        ["AccessOperand"] = ShapeNode.Obj(required: "source", members: new[] { ("assertions", ShapeNode.Arr(ShapeNode.Ref("AssertionReference"))) }),
        ["AccessRequirementsDescription"] = ShapeNode.Obj(required: "assertions assertionDisplays access scheme appliedRules rules combinedRequirementText combinedAttributesNeeded lines", members: new[] { ("assertions", ShapeNode.Arr(ShapeNode.Ref("Assertion"))), ("assertionDisplays", ShapeNode.Arr(null)), ("access", ShapeNode.Ref("ArtifactReference")), ("scheme", ShapeNode.Ref("ArtifactReference")), ("appliedRules", ShapeNode.Arr(ShapeNode.Ref("RuleReference"))), ("rules", ShapeNode.Arr(ShapeNode.Ref("AccessRuleDescription"))), ("combinedAttributesNeeded", ShapeNode.Arr(null)), ("lines", ShapeNode.Arr(null)) }),
        ["AccessRuleActivation"] = ShapeNode.Obj(required: "matched assertions contextPaths", members: new[] { ("assertions", ShapeNode.Arr(ShapeNode.Ref("AssertionReference"))), ("contextPaths", ShapeNode.Arr(null)) }),
        ["AccessRuleDescription"] = ShapeNode.Obj(required: "rule access scheme name requirementText attributesNeeded dependsOn overrides lines", members: new[] { ("rule", ShapeNode.Ref("RuleReference")), ("access", ShapeNode.Ref("ArtifactReference")), ("scheme", ShapeNode.Ref("ArtifactReference")), ("attributesNeeded", ShapeNode.Arr(null)), ("dependsOn", ShapeNode.Arr(ShapeNode.Ref("RuleReference"))), ("overrides", ShapeNode.Arr(ShapeNode.Ref("RuleReference"))), ("lines", ShapeNode.Arr(null)) }),
        ["AccessRuleResult"] = ShapeNode.Obj(required: "ruleIndex name granted skipped activation comparisons rule", members: new[] { ("activation", ShapeNode.Ref("AccessRuleActivation")), ("comparisons", ShapeNode.Arr(ShapeNode.Ref("AccessComparisonResult"))), ("rule", ShapeNode.Ref("RuleReference")), ("modernizedFrom", ShapeNode.Arr(ShapeNode.Ref("ConceptModernization"))) }),
        ["AccessScenarioAssessment"] = ShapeNode.Obj(required: "basis advisory status target limitations", nullable: "target", members: new[] { ("target", ShapeNode.Any(false, (ShapeNode.Ref("EffectiveDocumentTarget"), null))), ("decision", ShapeNode.Ref("AccessDecision")), ("contentRequirements", ShapeNode.Arr(ShapeNode.Ref("ContentChangeRequirement"))), ("contextRequirements", ShapeNode.Arr(ShapeNode.Ref("SubjectContextRequirement"))), ("limitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))) }),
        ["AggregateResult"] = ShapeNode.Obj(required: "assertions rendered valid errors warnings appliedFixes proposedFixes notices", members: new[] { ("assertions", ShapeNode.Arr(ShapeNode.Ref("Assertion"))), ("displayColor", ShapeNode.Ref("ConceptColor")), ("errors", ShapeNode.Arr(ShapeNode.Ref("ValidationError"))), ("warnings", ShapeNode.Arr(ShapeNode.Ref("Warning"))), ("appliedFixes", ShapeNode.Arr(ShapeNode.Ref("Change"))), ("proposedFixes", ShapeNode.Arr(ShapeNode.Ref("Fix"))), ("notices", ShapeNode.Arr(ShapeNode.Ref("Notice"))) }),
        ["ArtifactReference"] = ShapeNode.Obj(required: "kind id version"),
        ["Assertion"] = ShapeNode.Obj(required: "categoryId conceptId confidence source", members: new[] { ("provenance", ShapeNode.Ref("SourceReference")) }),
        ["AssertionRef"] = ShapeNode.Obj(required: "conceptId categoryId"),
        ["AssertionReference"] = ShapeNode.Obj(required: "categoryId conceptId"),
        ["AssertionSupport"] = ShapeNode.Obj(required: "supportId assertion rules evidence factReferences", members: new[] { ("assertion", ShapeNode.Ref("AssertionReference")), ("rules", ShapeNode.Arr(ShapeNode.Ref("RuleReference"))), ("evidence", ShapeNode.Arr(ShapeNode.Ref("ContentEvidenceSpan"))), ("factReferences", ShapeNode.Arr(ShapeNode.Ref("GuideFactReference"))) }),
        ["AssessmentLimitation"] = ShapeNode.Obj(required: "code reason"),
        ["Attribution"] = ShapeNode.Obj(required: "name license"),
        ["BestEffortRuleSourceReference"] = ShapeNode.Obj(required: "providerId reproducibility"),
        ["BestEffortSourceReference"] = ShapeNode.Obj(required: "attribution providerId reproducibility", members: new[] { ("attribution", ShapeNode.Ref("Attribution")) }),
        ["CalendarDateValueEncoding"] = ShapeNode.Obj(required: "type canonical accepts", members: new[] { ("accepts", ShapeNode.Arr(null)) }),
        ["CategoryEachAffixMap"] = ShapeNode.Map(ShapeNode.Ref("EachAffix")),
        ["CategoryTierMap"] = ShapeNode.Map(ShapeNode.Arr(ShapeNode.Ref("TierComposition"))),
        ["CategoryView"] = ShapeNode.Obj(required: "id label rollup", members: new[] { ("sort", ShapeNode.Any(true, (ShapeNode.Arr(null), null))), ("scopes", ShapeNode.Arr(null)), ("defaultConcept", ShapeNode.Any(true, (ShapeNode.Ref("ConditionalDefaultConcept"), null))) }),
        ["Change"] = ShapeNode.Obj(required: "ruleId action message", members: new[] { ("add", ShapeNode.Arr(ShapeNode.Ref("FixConceptReference"))), ("remove", ShapeNode.Arr(ShapeNode.Ref("FixConceptReference"))), ("triggerConceptIds", ShapeNode.Arr(null)) }),
        ["CharacterWindow"] = ShapeNode.Obj(required: "chars"),
        ["CheckCoverageResult"] = ShapeNode.Obj(required: "covered missing missingLabels excess excessLabels rendered", members: new[] { ("missing", ShapeNode.Arr(ShapeNode.Ref("Assertion"))), ("missingLabels", ShapeNode.Arr(null)), ("excess", ShapeNode.Arr(ShapeNode.Ref("Assertion"))), ("excessLabels", ShapeNode.Arr(null)), ("refusals", ShapeNode.Arr(ShapeNode.Ref("ValidationError"))) }),
        ["CompositeEncodingField"] = ShapeNode.Obj(required: "name type", members: new[] { ("concepts", ShapeNode.Arr(null)) }),
        ["CompositeValueEncoding"] = ShapeNode.Obj(required: "type fields", members: new[] { ("fields", ShapeNode.Arr(ShapeNode.Ref("CompositeEncodingField"))) }),
        ["ConceptColor"] = ShapeNode.Obj(required: "background text"),
        ["ConceptDescription"] = ShapeNode.Obj(required: "conceptId categoryId name lines", members: new[] { ("rules", ShapeNode.Arr(null)), ("lines", ShapeNode.Arr(null)) }),
        ["ConceptModernization"] = ShapeNode.Obj(required: "conceptId replacedConceptId conceptDisplay replacedConceptDisplay"),
        ["ConceptNotice"] = ShapeNode.Obj(required: "type required text"),
        ["ConceptRef"] = ShapeNode.Obj(required: "id"),
        ["ConceptView"] = ShapeNode.Obj(required: "id name views aliases categories parents position", members: new[] { ("color", ShapeNode.Ref("ConceptColor")), ("aliases", ShapeNode.Arr(null)), ("categories", ShapeNode.Arr(null)), ("parents", ShapeNode.Arr(null)), ("members", ShapeNode.Arr(null)), ("scopes", ShapeNode.Arr(null)), ("notices", ShapeNode.Arr(ShapeNode.Ref("ConceptNotice"))), ("rulesReferencing", ShapeNode.Arr(null)), ("textInput", ShapeNode.Obj(required: "", members: new[] { ("encoding", ShapeNode.Ref("ValueEncoding")) })) }),
        ["ConditionalDefaultConcept"] = ShapeNode.Obj(required: "concept when", members: new[] { ("when", ShapeNode.Ref("RuleWhen")) }),
        ["ContentChangeRequirement"] = ShapeNode.Obj(required: "rule assertion supportIds instruction verificationRequired", members: new[] { ("rule", ShapeNode.Ref("RuleReference")), ("assertion", ShapeNode.Ref("AssertionReference")), ("supportIds", ShapeNode.Arr(null)) }),
        ["ContentEvidenceSpan"] = ShapeNode.Obj(required: "start end offsetEncoding rule", members: new[] { ("rule", ShapeNode.Ref("RuleReference")) }),
        ["ContextAccessDecisionResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("AccessDecision")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextAccessRequirementsResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("AccessRequirementsDescription")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextAccessRuleDescriptionResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("AccessRuleDescription")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextCrosswalkTranslationResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("CrosswalkTranslationResult")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextDocumentAccessAssessmentResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("DocumentAccessAssessment")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextDocumentAuditResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("DocumentAuditReport")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextGuideEvaluationResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("EvaluationResult")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextGuideFilterRulesResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("FilterResult")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextGuideRuleDescriptionResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("RuleDescription")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextGuideScanResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Arr(ShapeNode.Ref("GuideScanResult"))), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextGuideViewResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("GuideViewResponse")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextResolution"] = ShapeNode.Obj(required: "contextId resolvedVersion artifacts sources", members: new[] { ("artifacts", ShapeNode.Arr(ShapeNode.Ref("ArtifactReference"))), ("sources", ShapeNode.Arr(ShapeNode.Ref("SourceReference"))) }),
        ["ContextSchemeAggregateResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("AggregateResult")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextSchemeConceptDescriptionResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("ConceptDescription")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextSchemeCoverageResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("CheckCoverageResult")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextSchemeDescriptor"] = ShapeNode.Obj(required: "selector id version roles", members: new[] { ("roles", ShapeNode.Arr(null)) }),
        ["ContextSchemeDiffResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("DiffResult")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextSchemeExplainResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("ExplanationTrace")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextSchemeFilterConceptsResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("FilterResult")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextSchemeNormalizeResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("NormalizeResponse")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextSchemeNoticesResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Arr(ShapeNode.Ref("Notice"))), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextSchemeProcessResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("ProcessResult")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextSchemeRenderResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("RenderResponse")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextSchemeScanResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("ScanResponseBody")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextSchemeSuggestForCategoriesResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("SuggestResultsByCategory")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextSchemeSuggestResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("SuggestResult")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextSchemeValidateResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("ValidateResult")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["ContextSchemeViewResult"] = ShapeNode.Obj(required: "resolution result operationLimitations", members: new[] { ("resolution", ShapeNode.Ref("ContextResolution")), ("result", ShapeNode.Ref("SchemeViewResponse")), ("operationLimitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))), ("responseProjection", ShapeNode.Ref("DocumentResponseProjection")) }),
        ["CrosswalkCompletedTranslationResult"] = ShapeNode.Obj(required: "crosswalk outcomes selections source sourceScheme status target targetScheme", members: new[] { ("crosswalk", ShapeNode.Ref("ArtifactReference")), ("sourceScheme", ShapeNode.Ref("ArtifactReference")), ("targetScheme", ShapeNode.Ref("ArtifactReference")), ("source", ShapeNode.Ref("ValidateResult")), ("outcomes", ShapeNode.Arr(ShapeNode.Ref("CrosswalkOutcome"))), ("selections", ShapeNode.Arr(ShapeNode.Ref("CrosswalkSelectionOutcome"))), ("target", ShapeNode.Ref("ValidateResult")) }),
        ["CrosswalkForbidMatch"] = ShapeNode.Any(false, (ShapeNode.Ref("CrosswalkMappingForbidMatch"), new[] { ("kind", "mapping") }), (ShapeNode.Ref("CrosswalkSelectionForbidMatch"), new[] { ("kind", "selection-candidate") })),
        ["CrosswalkForbiddenTranslationResult"] = ShapeNode.Obj(required: "crosswalk forbiddenBy outcomes selections source sourceScheme status targetScheme", members: new[] { ("crosswalk", ShapeNode.Ref("ArtifactReference")), ("sourceScheme", ShapeNode.Ref("ArtifactReference")), ("targetScheme", ShapeNode.Ref("ArtifactReference")), ("source", ShapeNode.Ref("ValidateResult")), ("outcomes", ShapeNode.Arr(ShapeNode.Ref("CrosswalkOutcome"))), ("selections", ShapeNode.Arr(ShapeNode.Ref("CrosswalkSelectionOutcome"))), ("forbiddenBy", ShapeNode.Arr(ShapeNode.Ref("CrosswalkForbidMatch"))) }),
        ["CrosswalkMappingForbidMatch"] = ShapeNode.Obj(required: "kind mappingId source reason", members: new[] { ("source", ShapeNode.Ref("AssertionReference")) }),
        ["CrosswalkOutcome"] = ShapeNode.Obj(required: "source disposition targets reason", members: new[] { ("source", ShapeNode.Ref("AssertionReference")), ("targets", ShapeNode.Arr(ShapeNode.Ref("CrosswalkTarget"))) }),
        ["CrosswalkSelectionForbidMatch"] = ShapeNode.Obj(required: "kind selectionId candidateId sources reason", members: new[] { ("sources", ShapeNode.Arr(ShapeNode.Ref("AssertionReference"))) }),
        ["CrosswalkSelectionOutcome"] = ShapeNode.Obj(required: "selectionId candidateId disposition targets reason", members: new[] { ("targets", ShapeNode.Arr(ShapeNode.Ref("CrosswalkTarget"))) }),
        ["CrosswalkTarget"] = ShapeNode.Obj(required: "categoryId conceptId"),
        ["CrosswalkTranslationResult"] = ShapeNode.Any(false, (ShapeNode.Ref("CrosswalkCompletedTranslationResult"), null), (ShapeNode.Ref("CrosswalkForbiddenTranslationResult"), new[] { ("status", "forbidden") })),
        ["CtxAccessRulesResponseBody"] = ShapeNode.Obj(required: "rules", members: new[] { ("rules", ShapeNode.Arr(ShapeNode.Ref("AccessRuleDescription"))) }),
        ["CtxComponentDescriptor"] = ShapeNode.Obj(required: "id version resolvedVersion", members: new[] { ("layers", ShapeNode.Arr(ShapeNode.Ref("CtxComponentDescriptor"))) }),
        ["CtxDescribeResponseBody"] = ShapeNode.Obj(required: "id version scheme schemes", members: new[] { ("scheme", ShapeNode.Ref("CtxComponentDescriptor")), ("schemes", ShapeNode.Arr(ShapeNode.Ref("ContextSchemeDescriptor"))), ("guides", ShapeNode.Arr(ShapeNode.Ref("CtxComponentDescriptor"))), ("access", ShapeNode.Ref("CtxComponentDescriptor")), ("crosswalks", ShapeNode.Arr(ShapeNode.Ref("CtxComponentDescriptor"))) }),
        ["CtxListResponseBody"] = ShapeNode.Obj(required: "contexts", members: new[] { ("contexts", ShapeNode.Arr(null)) }),
        ["CtxVersionsResponseBody"] = ShapeNode.Obj(required: "id versions", members: new[] { ("versions", ShapeNode.Arr(null)) }),
        ["DiffResult"] = ShapeNode.Obj(required: "added removed common", members: new[] { ("added", ShapeNode.Arr(ShapeNode.Ref("Assertion"))), ("removed", ShapeNode.Arr(ShapeNode.Ref("Assertion"))), ("common", ShapeNode.Arr(ShapeNode.Ref("Assertion"))) }),
        ["DocumentAccessAssessment"] = ShapeNode.Obj(required: "audit authored authoritative projected", nullable: "projected", members: new[] { ("audit", ShapeNode.Ref("DocumentAuditReport")), ("authored", ShapeNode.Ref("AccessScenarioAssessment")), ("authoritative", ShapeNode.Ref("AccessScenarioAssessment")), ("projected", ShapeNode.Any(false, (ShapeNode.Ref("AccessScenarioAssessment"), null))) }),
        ["DocumentAuditReport"] = ShapeNode.Obj(required: "markingRequirements whole wholes wholeValidation parts wholeCoversParts unmarkedSpans proceduralFlags overallVerdict vocabulary documentDigest guideAssessment limitations", nullable: "whole wholeValidation wholeCoversParts vocabulary guideAssessment", members: new[] { ("markingRequirements", ShapeNode.Ref("DocumentMarkingRequirements")), ("wholes", ShapeNode.Arr(ShapeNode.Ref("DocumentWholeOccurrence"))), ("wholeValidation", ShapeNode.Any(false, (ShapeNode.Ref("MarkingValidation"), null))), ("parts", ShapeNode.Arr(ShapeNode.Ref("DocumentPartAssessment"))), ("wholeCoversParts", ShapeNode.Any(false, (ShapeNode.Ref("CheckCoverageResult"), null))), ("unmarkedSpans", ShapeNode.Arr(ShapeNode.Ref("DocumentUnmarkedSpan"))), ("proceduralFlags", ShapeNode.Arr(null)), ("vocabulary", ShapeNode.Any(false, (ShapeNode.Ref("Vocabulary"), null))), ("guideAssessment", ShapeNode.Any(false, (ShapeNode.Ref("DocumentGuideAssessment"), null))), ("limitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))) }),
        ["DocumentGuideAssessment"] = ShapeNode.Obj(required: "status nominationCount unresolvedCount acceptedCount rejectedCount evidenceSpanCount unitCoverage determinedRequiredWhole projectedRequiredWhole determinedWholeCoverage projectedWholeCoverage", nullable: "determinedRequiredWhole projectedRequiredWhole determinedWholeCoverage projectedWholeCoverage", members: new[] { ("evaluation", ShapeNode.Ref("GuideContentEvaluation")), ("unitCoverage", ShapeNode.Arr(ShapeNode.Ref("GuideScopeCoverage"))), ("determinedRequiredWhole", ShapeNode.Any(false, (ShapeNode.Ref("GuideCompiledResult"), null))), ("projectedRequiredWhole", ShapeNode.Any(false, (ShapeNode.Ref("GuideCompiledResult"), null))), ("determinedWholeCoverage", ShapeNode.Any(false, (ShapeNode.Ref("CheckCoverageResult"), null))), ("projectedWholeCoverage", ShapeNode.Any(false, (ShapeNode.Ref("CheckCoverageResult"), null))) }),
        ["DocumentMarkingRequirements"] = ShapeNode.Obj(required: "whole part"),
        ["DocumentPartAssessment"] = ShapeNode.Obj(required: "marking start end offsetEncoding scopeId validation", members: new[] { ("validation", ShapeNode.Ref("MarkingValidation")) }),
        ["DocumentResponseCounts"] = ShapeNode.Obj(required: "nominations evidenceSpans guideUnits accessRuleResults contentRequirements contextRequirements"),
        ["DocumentResponseProjection"] = ShapeNode.Obj(required: "profile detailsComplete omittedFields counts", members: new[] { ("omittedFields", ShapeNode.Arr(null)), ("counts", ShapeNode.Ref("DocumentResponseCounts")) }),
        ["DocumentUnmarkedSpan"] = ShapeNode.Obj(required: "start end offsetEncoding"),
        ["DocumentWholeOccurrence"] = ShapeNode.Obj(required: "marking start end offsetEncoding"),
        ["EachAffix"] = ShapeNode.Obj(required: ""),
        ["EffectiveDocumentTarget"] = ShapeNode.Obj(required: "basis marking assertions authoredParts", members: new[] { ("assertions", ShapeNode.Arr(ShapeNode.Ref("Assertion"))), ("authoredParts", ShapeNode.Arr(null)) }),
        ["EmbeddedMarkingStyle"] = ShapeNode.Obj(required: "pattern template"),
        ["EvaluationResult"] = ShapeNode.Obj(required: "selectedRules activeComputedRules effectiveMarkings aggregatedMarking breakdown contributingBreakdown", members: new[] { ("selectedRules", ShapeNode.Arr(ShapeNode.Ref("GuideRule"))), ("activeComputedRules", ShapeNode.Arr(ShapeNode.Ref("GuideRule"))), ("effectiveMarkings", ShapeNode.Arr(null)), ("breakdown", ShapeNode.Arr(ShapeNode.Ref("GuideBreakdownEntry"))), ("contributingBreakdown", ShapeNode.Arr(ShapeNode.Ref("GuideBreakdownEntry"))), ("provenance", ShapeNode.Ref("SourceReference")) }),
        ["ExplanationTrace"] = ShapeNode.Obj(required: "input schemeId entries repairs validationErrors inferences finalAssertions rendered", members: new[] { ("entries", ShapeNode.Arr(ShapeNode.Ref("TraceEntry"))), ("repairs", ShapeNode.Arr(ShapeNode.Ref("TraceRepair"))), ("validationErrors", ShapeNode.Arr(ShapeNode.Ref("TraceValidationIssue"))), ("inferences", ShapeNode.Arr(ShapeNode.Ref("TraceInference"))), ("finalAssertions", ShapeNode.Arr(ShapeNode.Ref("Assertion"))) }),
        ["FilterResult"] = ShapeNode.Obj(required: "visibleIds categoryCounts total", members: new[] { ("visibleIds", ShapeNode.Arr(null)), ("categoryCounts", ShapeNode.Map(ShapeNode.Obj(required: "visible total"))), ("total", ShapeNode.Obj(required: "visible total")) }),
        ["Fix"] = ShapeNode.Obj(required: "message rendered keeps adds removes", members: new[] { ("keeps", ShapeNode.Arr(ShapeNode.Ref("FixConceptReference"))), ("adds", ShapeNode.Arr(ShapeNode.Ref("FixConceptReference"))), ("removes", ShapeNode.Arr(ShapeNode.Ref("FixConceptReference"))) }),
        ["FixConceptReference"] = ShapeNode.Obj(required: "conceptId categoryId"),
        ["FormatHeaderAssociation"] = ShapeNode.Obj(required: "concept headerCategory category position"),
        ["FormatSyntax"] = ShapeNode.Obj(required: "type"),
        ["FormatView"] = ShapeNode.Obj(required: "categoryOrder id mode surface", members: new[] { ("defaultFor", ShapeNode.Arr(null)), ("categoryOrder", ShapeNode.Arr(null)), ("categoryTiers", ShapeNode.Ref("CategoryTierMap")), ("categoryEach", ShapeNode.Ref("CategoryEachAffixMap")), ("headers", ShapeNode.Arr(ShapeNode.Ref("FormatHeaderAssociation"))), ("syntax", ShapeNode.Ref("FormatSyntax")) }),
        ["GuideBreakdownEntry"] = ShapeNode.Obj(required: "rule outcome isComputed wasOverridden", members: new[] { ("rule", ShapeNode.Ref("RuleReference")), ("outcome", ShapeNode.Ref("GuideRuleOutcome")), ("overriddenBy", ShapeNode.Ref("RuleReference")) }),
        ["GuideCompiledDetail"] = ShapeNode.Obj(required: "evaluation process", members: new[] { ("evaluation", ShapeNode.Ref("EvaluationResult")), ("process", ShapeNode.Ref("ValidateResult")) }),
        ["GuideCompiledResult"] = ShapeNode.Obj(required: "assertions rendered valid errors warnings directRules computedRules overriddenRules supportIds", members: new[] { ("assertions", ShapeNode.Arr(ShapeNode.Ref("Assertion"))), ("errors", ShapeNode.Arr(ShapeNode.Ref("ValidationError"))), ("warnings", ShapeNode.Arr(ShapeNode.Ref("Warning"))), ("directRules", ShapeNode.Arr(ShapeNode.Ref("RuleReference"))), ("computedRules", ShapeNode.Arr(ShapeNode.Ref("RuleReference"))), ("overriddenRules", ShapeNode.Arr(ShapeNode.Ref("RuleReference"))), ("supportIds", ShapeNode.Arr(null)), ("detail", ShapeNode.Ref("GuideCompiledDetail")) }),
        ["GuideContentEvaluation"] = ShapeNode.Obj(required: "documentDigest nominations determinations manualSelections unresolvedFindingIds acceptedFindingIds rejectedFindingIds supportCatalog units limitations", members: new[] { ("nominations", ShapeNode.Arr(ShapeNode.Ref("GuideNomination"))), ("determinations", ShapeNode.Arr(ShapeNode.Ref("GuideFindingDetermination"))), ("manualSelections", ShapeNode.Arr(ShapeNode.Ref("ManualGuideSelection"))), ("unresolvedFindingIds", ShapeNode.Arr(null)), ("acceptedFindingIds", ShapeNode.Arr(null)), ("rejectedFindingIds", ShapeNode.Arr(null)), ("supportCatalog", ShapeNode.Arr(ShapeNode.Ref("AssertionSupport"))), ("units", ShapeNode.Arr(ShapeNode.Ref("GuideUnitEvaluation"))), ("limitations", ShapeNode.Arr(ShapeNode.Ref("AssessmentLimitation"))) }),
        ["GuideFactReference"] = ShapeNode.Obj(required: "kind factId"),
        ["GuideFindingDetermination"] = ShapeNode.Obj(required: "findingId documentDigest decision reason"),
        ["GuideMatchSpec"] = ShapeNode.Obj(required: "type"),
        ["GuideNomination"] = ShapeNode.Obj(required: "findingId documentDigest origin rule evidence proposedAssertions", members: new[] { ("rule", ShapeNode.Ref("RuleReference")), ("evidence", ShapeNode.Arr(ShapeNode.Ref("ContentEvidenceSpan"))), ("proposedAssertions", ShapeNode.Arr(ShapeNode.Ref("AssertionReference"))) }),
        ["GuideRule"] = ShapeNode.Obj(required: "id name outcome", members: new[] { ("keywords", ShapeNode.Arr(null)), ("match", ShapeNode.Arr(ShapeNode.Ref("MatchNode"))), ("outcome", ShapeNode.Ref("GuideRuleOutcome")), ("when", ShapeNode.Ref("RuleWhen")), ("overrides", ShapeNode.Arr(null)) }),
        ["GuideRuleOutcome"] = ShapeNode.Obj(required: "", members: new[] { ("concepts", ShapeNode.Arr(ShapeNode.Ref("ConceptRef"))) }),
        ["GuideRuleView"] = ShapeNode.Obj(required: "id name outcomeText isComputed keywords matcherLabels dependsOn dependsOnNames overrides overrideNames", members: new[] { ("keywords", ShapeNode.Arr(null)), ("matcherLabels", ShapeNode.Arr(null)), ("dependsOn", ShapeNode.Arr(null)), ("dependsOnNames", ShapeNode.Arr(null)), ("overrides", ShapeNode.Arr(null)), ("overrideNames", ShapeNode.Arr(null)) }),
        ["GuideScanResult"] = ShapeNode.Obj(required: "rule matchedKeywords spans", members: new[] { ("rule", ShapeNode.Ref("GuideRule")), ("matchedKeywords", ShapeNode.Arr(null)), ("spans", ShapeNode.Arr(ShapeNode.Ref("GuideScanSpan"))), ("provenance", ShapeNode.Ref("SourceReference")) }),
        ["GuideScanSpan"] = ShapeNode.Obj(required: "keyword start end"),
        ["GuideScopeCoverage"] = ShapeNode.Obj(required: "scopeId determinedCoverage projectedCoverage", nullable: "determinedCoverage projectedCoverage", members: new[] { ("determinedCoverage", ShapeNode.Any(false, (ShapeNode.Ref("CheckCoverageResult"), null))), ("projectedCoverage", ShapeNode.Any(false, (ShapeNode.Ref("CheckCoverageResult"), null))) }),
        ["GuideUnitEvaluation"] = ShapeNode.Obj(required: "determined end findingIds offsetEncoding projected scopeId start", nullable: "determined projected", members: new[] { ("findingIds", ShapeNode.Arr(null)), ("determined", ShapeNode.Any(false, (ShapeNode.Ref("GuideCompiledResult"), null))), ("projected", ShapeNode.Any(false, (ShapeNode.Ref("GuideCompiledResult"), null))) }),
        ["GuideViewResponse"] = ShapeNode.Obj(required: "id name description categories uncategorized", members: new[] { ("categories", ShapeNode.Arr(ShapeNode.Obj(required: "id label items", members: new[] { ("items", ShapeNode.Arr(ShapeNode.Ref("GuideRuleView"))) }))), ("uncategorized", ShapeNode.Arr(ShapeNode.Ref("GuideRuleView"))), ("vocabulary", ShapeNode.Ref("Vocabulary")) }),
        ["HealthResponse"] = ShapeNode.Obj(required: "status engineVersion engineSha gitTag openapiVersion abiVersion"),
        ["InputSpan"] = ShapeNode.Obj(required: "start end text"),
        ["LabelPresetView"] = ShapeNode.Obj(required: "id label"),
        ["ManualGuideSelection"] = ShapeNode.Obj(required: "documentDigest evidence reason rule selectionId", members: new[] { ("rule", ShapeNode.Ref("RuleReference")), ("evidence", ShapeNode.Arr(ShapeNode.Ref("ContentEvidenceSpan"))) }),
        ["MarkingColorRule"] = ShapeNode.Obj(required: "color", members: new[] { ("when", ShapeNode.Ref("RuleWhen")), ("color", ShapeNode.Ref("ConceptColor")) }),
        ["MarkingValidation"] = ShapeNode.Obj(required: "assertions recognition valid inputWasValid errors proposedFixes appliedFixes rendered", members: new[] { ("assertions", ShapeNode.Arr(ShapeNode.Ref("Assertion"))), ("recognition", ShapeNode.Ref("RecognitionResult")), ("errors", ShapeNode.Arr(ShapeNode.Ref("ValidationError"))), ("proposedFixes", ShapeNode.Arr(ShapeNode.Ref("Fix"))), ("appliedFixes", ShapeNode.Arr(ShapeNode.Ref("Change"))) }),
        ["MatchAllNode"] = ShapeNode.Obj(required: "all within", members: new[] { ("all", ShapeNode.Arr(ShapeNode.Ref("MatchNode"))), ("within", ShapeNode.Ref("Win")) }),
        ["MatchAnyNode"] = ShapeNode.Obj(required: "any", members: new[] { ("any", ShapeNode.Arr(ShapeNode.Ref("MatchNode"))) }),
        ["MatchContextExpression"] = ShapeNode.Obj(required: "candidate cue within", members: new[] { ("candidate", ShapeNode.Ref("MatchNode")), ("cue", ShapeNode.Ref("MatchNode")), ("within", ShapeNode.Ref("Win")) }),
        ["MatchContextNode"] = ShapeNode.Obj(required: "context", members: new[] { ("context", ShapeNode.Ref("MatchContextExpression")) }),
        ["MatchNearExpression"] = ShapeNode.Obj(required: "a b within", members: new[] { ("a", ShapeNode.Ref("MatchNode")), ("b", ShapeNode.Ref("MatchNode")), ("within", ShapeNode.Ref("Win")) }),
        ["MatchNearNode"] = ShapeNode.Obj(required: "near", members: new[] { ("near", ShapeNode.Ref("MatchNearExpression")) }),
        ["MatchNode"] = ShapeNode.Any(false, (ShapeNode.Ref("GuideMatchSpec"), null), (ShapeNode.Ref("MatchAnyNode"), null), (ShapeNode.Ref("MatchAllNode"), null), (ShapeNode.Ref("MatchNotNode"), null), (ShapeNode.Ref("MatchNearNode"), null), (ShapeNode.Ref("MatchContextNode"), null), (ShapeNode.Ref("MatchValidateNode"), null)),
        ["MatchNotNode"] = ShapeNode.Obj(required: "not of within", members: new[] { ("not", ShapeNode.Ref("MatchNode")), ("of", ShapeNode.Ref("MatchNode")), ("within", ShapeNode.Ref("Win")) }),
        ["MatchValidateNode"] = ShapeNode.Obj(required: "validate", members: new[] { ("validate", ShapeNode.Ref("MatchValidationExpression")) }),
        ["MatchValidationExpression"] = ShapeNode.Obj(required: "candidate with", members: new[] { ("candidate", ShapeNode.Ref("MatchNode")) }),
        ["NormalizeResponse"] = ShapeNode.Obj(required: "result"),
        ["Notice"] = ShapeNode.Obj(required: "conceptId conceptName type text required"),
        ["ProcessResult"] = ShapeNode.Obj(required: "appliedFixes assertions assessment errors inputWasValid notices proposedFixes rendered renders valid warnings", members: new[] { ("assertions", ShapeNode.Arr(ShapeNode.Ref("Assertion"))), ("errors", ShapeNode.Arr(ShapeNode.Ref("ValidationError"))), ("warnings", ShapeNode.Arr(ShapeNode.Ref("Warning"))), ("appliedFixes", ShapeNode.Arr(ShapeNode.Ref("Change"))), ("assessment", ShapeNode.Ref("RenderAssessment")), ("recognition", ShapeNode.Ref("RecognitionWithCorrections")), ("notices", ShapeNode.Arr(ShapeNode.Ref("Notice"))), ("displayColor", ShapeNode.Ref("ConceptColor")), ("assertionsBeforeRepair", ShapeNode.Arr(ShapeNode.Ref("Assertion"))), ("proposedFixes", ShapeNode.Arr(ShapeNode.Ref("Fix"))), ("renders", ShapeNode.Ref("RenderResultsByFormat")), ("suggestionsFor", ShapeNode.Ref("SuggestResultsByCategory")) }),
        ["ReadyResponse"] = ShapeNode.Obj(required: "status", members: new[] { ("warnings", ShapeNode.Arr(null)) }),
        ["RecognitionCorrection"] = ShapeNode.Obj(required: "start end original replacement reason"),
        ["RecognitionResult"] = ShapeNode.Obj(required: "status unmatched", members: new[] { ("unmatched", ShapeNode.Arr(ShapeNode.Ref("InputSpan"))) }),
        ["RecognitionWithCorrections"] = ShapeNode.Obj(required: "corrections status unmatched", members: new[] { ("unmatched", ShapeNode.Arr(ShapeNode.Ref("InputSpan"))), ("corrections", ShapeNode.Arr(ShapeNode.Ref("RecognitionCorrection"))) }),
        ["ReferenceSurfaceLabel"] = ShapeNode.Obj(required: "singular plural"),
        ["RenderAssessment"] = ShapeNode.Obj(required: "emitted omitted collapsed conflicts unrepresentable", members: new[] { ("emitted", ShapeNode.Arr(ShapeNode.Ref("AssertionRef"))), ("omitted", ShapeNode.Arr(ShapeNode.Ref("AssertionRef"))), ("collapsed", ShapeNode.Arr(ShapeNode.Arr(ShapeNode.Ref("AssertionRef")))), ("conflicts", ShapeNode.Arr(ShapeNode.Ref("RenderFieldConflict"))), ("unrepresentable", ShapeNode.Arr(ShapeNode.Ref("UnrepresentableValue"))) }),
        ["RenderFieldConflict"] = ShapeNode.Obj(required: "field assertions", members: new[] { ("assertions", ShapeNode.Arr(ShapeNode.Ref("AssertionRef"))) }),
        ["RenderResponse"] = ShapeNode.Obj(required: "result assessment", members: new[] { ("assessment", ShapeNode.Ref("RenderAssessment")) }),
        ["RenderResult"] = ShapeNode.Obj(required: "rendered assessment", members: new[] { ("assessment", ShapeNode.Ref("RenderAssessment")) }),
        ["RenderResultsByFormat"] = ShapeNode.Map(ShapeNode.Ref("RenderResult")),
        ["ReproducibleRuleSourceReference"] = ShapeNode.Obj(required: "providerId reproducibility snapshotId"),
        ["ReproducibleSourceReference"] = ShapeNode.Obj(required: "attribution providerId reproducibility snapshotId", members: new[] { ("attribution", ShapeNode.Ref("Attribution")) }),
        ["RuleCondition"] = ShapeNode.Obj(required: ""),
        ["RuleDescription"] = ShapeNode.Obj(required: "rule guide name outcomeText isComputed dependsOn overrides lines", members: new[] { ("rule", ShapeNode.Ref("RuleReference")), ("guide", ShapeNode.Ref("ArtifactReference")), ("dependsOn", ShapeNode.Arr(ShapeNode.Ref("RuleReference"))), ("overrides", ShapeNode.Arr(ShapeNode.Ref("RuleReference"))), ("lines", ShapeNode.Arr(null)) }),
        ["RuleReference"] = ShapeNode.Obj(required: "artifact ruleId ruleDigest", members: new[] { ("artifact", ShapeNode.Ref("ArtifactReference")), ("source", ShapeNode.Ref("RuleSourceReference")) }),
        ["RuleSourceReference"] = ShapeNode.Any(false, (ShapeNode.Ref("ReproducibleRuleSourceReference"), new[] { ("reproducibility", "reproducible") }), (ShapeNode.Ref("BestEffortRuleSourceReference"), new[] { ("reproducibility", "best-effort") })),
        ["RuleWhen"] = ShapeNode.Any(true, (ShapeNode.Arr(null), null), (ShapeNode.Ref("RuleWhenQuantifier"), new[] { ("target", "contributors") })),
        ["RuleWhenNode"] = ShapeNode.Any(false, (ShapeNode.Ref("RuleCondition"), null), (ShapeNode.Ref("RuleWhenQuantifier"), new[] { ("target", "contributors") })),
        ["RuleWhenQuantifier"] = ShapeNode.Obj(required: "", members: new[] { ("any", ShapeNode.Arr(ShapeNode.Ref("RuleWhenNode"))), ("all", ShapeNode.Arr(ShapeNode.Ref("RuleWhenNode"))), ("none", ShapeNode.Arr(ShapeNode.Ref("RuleWhenNode"))) }),
        ["ScanLimits"] = ShapeNode.Obj(required: "status regionsFound"),
        ["ScanResponseBody"] = ShapeNode.Obj(required: "results limits", members: new[] { ("results", ShapeNode.Arr(ShapeNode.Ref("ScanResult"))), ("limits", ShapeNode.Ref("ScanLimits")) }),
        ["ScanResult"] = ShapeNode.Obj(required: "marking start end result type wrapped role", members: new[] { ("result", ShapeNode.Ref("ValidateResult")) }),
        ["SchemeGrammar"] = ShapeNode.Obj(required: "caseSensitive stripWhitespace"),
        ["SchemeScope"] = ShapeNode.Obj(required: "id label"),
        ["SchemeScopesResponse"] = ShapeNode.Arr(ShapeNode.Ref("SchemeScope")),
        ["SchemeSummary"] = ShapeNode.Obj(required: "id version name description referenceSurfaceLabel", members: new[] { ("colorRules", ShapeNode.Arr(ShapeNode.Ref("MarkingColorRule"))), ("vocabulary", ShapeNode.Ref("Vocabulary")), ("referenceSurfaceLabel", ShapeNode.Ref("ReferenceSurfaceLabel")), ("contextFormats", ShapeNode.Obj(required: "whole part", members: new[] { ("whole", ShapeNode.Arr(null)), ("part", ShapeNode.Arr(null)) })), ("embeddedMarking", ShapeNode.Arr(ShapeNode.Ref("EmbeddedMarkingStyle"))) }),
        ["SchemeViewResponse"] = ShapeNode.Obj(required: "id name description categories uncategorized", members: new[] { ("categories", ShapeNode.Arr(ShapeNode.Obj(required: "id label items", members: new[] { ("items", ShapeNode.Arr(ShapeNode.Ref("ConceptView"))) }))), ("uncategorized", ShapeNode.Arr(ShapeNode.Ref("ConceptView"))), ("vocabulary", ShapeNode.Ref("Vocabulary")) }),
        ["SourceReference"] = ShapeNode.Any(false, (ShapeNode.Ref("ReproducibleSourceReference"), new[] { ("reproducibility", "reproducible") }), (ShapeNode.Ref("BestEffortSourceReference"), new[] { ("reproducibility", "best-effort") })),
        ["SubjectContextRequirement"] = ShapeNode.Obj(required: "rule subjectPath contextPath operator instruction", members: new[] { ("rule", ShapeNode.Ref("RuleReference")), ("acceptableValues", ShapeNode.Arr(null)) }),
        ["SuggestResult"] = ShapeNode.Obj(required: "suggestions warnings", members: new[] { ("suggestions", ShapeNode.Arr(ShapeNode.Ref("Suggestion"))), ("warnings", ShapeNode.Arr(ShapeNode.Ref("Warning"))) }),
        ["SuggestResultsByCategory"] = ShapeNode.Map(ShapeNode.Ref("SuggestResult")),
        ["Suggestion"] = ShapeNode.Obj(required: "conceptId categoryId label description name views group selected order", members: new[] { ("requires", ShapeNode.Arr(null)), ("requiresDisplay", ShapeNode.Arr(null)) }),
        ["Term"] = ShapeNode.Obj(required: ""),
        ["TextValueEncoding"] = ShapeNode.Obj(required: "type"),
        ["TierComposition"] = ShapeNode.Obj(required: "tier attachedBy"),
        ["TokenWindow"] = ShapeNode.Obj(required: "tokens"),
        ["TraceEntry"] = ShapeNode.Obj(required: "stage action detail", members: new[] { ("triggerConceptIds", ShapeNode.Arr(null)) }),
        ["TraceInference"] = ShapeNode.Obj(required: "ruleId conceptId explanation"),
        ["TraceRepair"] = ShapeNode.Obj(required: "ruleId description confidence before after"),
        ["TraceValidationIssue"] = ShapeNode.Obj(required: "constraintId message"),
        ["UnrepresentableFact"] = ShapeNode.Obj(required: "conceptId reason"),
        ["UnrepresentableOutput"] = ShapeNode.Obj(required: "output reason"),
        ["UnrepresentableValue"] = ShapeNode.Any(false, (ShapeNode.Ref("UnrepresentableFact"), null), (ShapeNode.Ref("UnrepresentableOutput"), null)),
        ["ValidateResult"] = ShapeNode.Obj(required: "assertions valid inputWasValid errors warnings appliedFixes rendered assessment notices", members: new[] { ("assertions", ShapeNode.Arr(ShapeNode.Ref("Assertion"))), ("errors", ShapeNode.Arr(ShapeNode.Ref("ValidationError"))), ("warnings", ShapeNode.Arr(ShapeNode.Ref("Warning"))), ("appliedFixes", ShapeNode.Arr(ShapeNode.Ref("Change"))), ("assessment", ShapeNode.Ref("RenderAssessment")), ("recognition", ShapeNode.Ref("RecognitionResult")), ("notices", ShapeNode.Arr(ShapeNode.Ref("Notice"))), ("displayColor", ShapeNode.Ref("ConceptColor")) }),
        ["ValidationError"] = ShapeNode.Obj(required: "constraintId message", members: new[] { ("affectedConcepts", ShapeNode.Arr(null)), ("didYouMean", ShapeNode.Arr(null)) }),
        ["ValueEncoding"] = ShapeNode.Any(false, (ShapeNode.Ref("TextValueEncoding"), new[] { ("type", "text") }), (ShapeNode.Ref("CalendarDateValueEncoding"), new[] { ("type", "calendar-date"), ("canonical", "YYYY-MM-DD") }), (ShapeNode.Ref("CompositeValueEncoding"), new[] { ("type", "composite") })),
        ["Vocabulary"] = ShapeNode.Map(ShapeNode.Ref("Term")),
        ["Warning"] = ShapeNode.Obj(required: "kind message"),
        ["Win"] = ShapeNode.Any(false, (ShapeNode.Ref("TokenWindow"), null), (ShapeNode.Ref("CharacterWindow"), null)),
    };
}
