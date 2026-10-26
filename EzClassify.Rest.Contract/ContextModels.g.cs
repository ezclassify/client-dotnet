// Code generated from the canonical OpenAPI contract (8c117d90e685aae85881ae0b5ac093af46653b90b885d38b1309309b37eab0f1) by gen.mjs@5cf243baa1066cacdc9db5123130bfc7cc034a1a04ecc014f5cbca1e4f02e6ba. DO NOT EDIT.
// Re-run via: make regen
#nullable enable

using System.Collections.Generic;
using System.Text.Json;

namespace EzClassify;

// Result shapes returned by the context-bound engine operations
// (/v1/ctx/{ctxId}/{version}/*). These mirror the adapter contracts in
// contracts' public types: CoverageReport, ConceptDescription, FilterResult,
// RuleDescription, the access description records, and the composed document
// audit report. This template is owned by gen.mjs and emitted as committed
// generated source after the generator verifies the canonical OpenAPI inputs.

// ---------------------------------------------------------------------------
// Typed breakdown/fix vocabulary (1.0 polyglot parity).
//
// These are the wire shapes for the fix and change buckets (Fix / Change /
// ConceptRef), the engine explanation trace (ExplanationTrace and its
// TraceEntry / TraceRepair / TraceInference / TraceValidationIssue
// sub-records), and the per-rule guide breakdown (GuideBreakdownEntry). They
// mirror libs/types/src/scheme.ts and the vendored json-schemas (fix, change,
// trace-entry, explanation-trace, guide-breakdown-entry).
//
// The typed family remains one coherent public surface while its shipped source
// is generated together with the operation models and the wire enums.

/// <summary>
/// A reference to a concept in a specific category. Both ids are required on the
/// wire. Carried by <see cref="Fix.Keeps"/>, <see cref="Fix.Adds"/>, and
/// <see cref="Fix.Removes"/>, and by <see cref="Change.Add"/> and
/// <see cref="Change.Remove"/>.
/// </summary>
/// <param name="ConceptId">Concept identifier.</param>
/// <param name="CategoryId">Owning category identifier.</param>
public record ConceptRef(
    string ConceptId,
    string CategoryId
);

/// <summary>
/// One way to make the marking valid. Every change in it answers an error.
/// Lives only in <c>proposedFixes</c>, including a document audit's
/// <c>MarkingValidation.proposedFixes</c>. <see cref="Rendered"/> is the valid
/// marking this fix produces; <see cref="Keeps"/> names concepts of the
/// author's marking it keeps, <see cref="Adds"/> concepts it adds because an
/// error requires them, and <see cref="Removes"/> concepts of the author's
/// marking it removes because an error names them.
/// </summary>
/// <param name="Message">What this fix does, in words.</param>
/// <param name="Rendered">The valid marking this fix produces, in the result's output format.</param>
/// <param name="Keeps">Concepts of the author's marking this fix keeps.</param>
/// <param name="Adds">Concepts this fix adds because an error requires them.</param>
/// <param name="Removes">Concepts of the author's marking this fix removes because an error names them.</param>
public record Fix(
    string Message,
    string Rendered,
    IReadOnlyList<ConceptRef> Keeps,
    IReadOnlyList<ConceptRef> Adds,
    IReadOnlyList<ConceptRef> Removes
);

/// <summary>
/// A change the engine made while processing. Lives in <c>appliedFixes</c>.
/// <see cref="Action"/> says which of <see cref="Add"/> and <see cref="Remove"/>
/// are set: <c>add</c> only <see cref="Add"/>, <c>remove</c> only
/// <see cref="Remove"/>, <c>replace</c> both, and <c>reorder</c> neither (the
/// input parsed via a tolerant rule; the canonical form is in
/// <c>ProcessResult.Rendered</c>).
/// </summary>
/// <param name="RuleId">Rule that made this change: a scheme rule's id, or an id the engine keeps for a change no scheme rule names.</param>
/// <param name="Action">'add' (only <c>add</c> set), 'remove' (only <c>remove</c> set), 'replace' (both), 'reorder' (neither: the input parsed via a tolerant rule; the canonical form is in the result's <c>rendered</c>).</param>
/// <param name="Message">What the change did, in words.</param>
/// <param name="Add">Concepts the change added. Set for <c>add</c> and <c>replace</c>.</param>
/// <param name="Remove">Concepts the change removed. Set for <c>remove</c> and <c>replace</c>.</param>
/// <param name="TriggerConceptIds">Concept IDs whose presence triggered the rule that made this change.</param>
public record Change(
    string RuleId,
    FixAction Action,
    string Message,
    IReadOnlyList<ConceptRef>? Add = null,
    IReadOnlyList<ConceptRef>? Remove = null,
    IReadOnlyList<string>? TriggerConceptIds = null
);

/// <summary>
/// One step in the engine's explanation trace: which stage acted, what it did,
/// and which concept/rule was involved. <see cref="Action"/> is a plain string
/// here (unlike the typed <see cref="FixAction"/>): the trace narrates every
/// pipeline stage and is not a contract value set.
/// </summary>
/// <param name="Stage">The pipeline stage that acted, such as <c>parse</c>, <c>normalize</c>, <c>repair</c>, <c>infer</c>, <c>validate</c>, <c>render</c>, <c>aggregate</c> or <c>context</c>. An open string.</param>
/// <param name="Action">What the stage did, as a short machine-readable token, such as <c>asserted</c>, <c>inferred</c>, <c>removed</c> or <c>unmatched</c>. An open string.</param>
/// <param name="Detail">A sentence describing the step for a person. It is prose and must not be parsed; the structured fields carry the ids.</param>
/// <param name="ConceptId">The concept the step acted on, when it acted on one.</param>
/// <param name="RuleId">The scheme rule that drove the step, when one did.</param>
/// <param name="TriggerConceptIds">Concept IDs that triggered the rule producing this entry.</param>
/// <param name="ConceptName">Resolved operational display for <c>conceptId</c> (the concept's rendered marking view), so a "why" breakdown UI can show the marking rather than the raw id. <c>conceptId</c> stays as the machine key.</param>
/// <param name="CategoryId">Category the action landed in, when the acting rule declared one. This is the structured channel for the category: <c>detail</c> is human-facing prose and must never be parsed to recover it.</param>
public record TraceEntry(
    string Stage,
    string Action,
    string Detail,
    string? ConceptId = null,
    string? RuleId = null,
    IReadOnlyList<string>? TriggerConceptIds = null,
    string? ConceptName = null,
    string? CategoryId = null
);

/// <summary>
/// One repair the engine applied while normalizing malformed input, with the
/// before/after strings. <see cref="Confidence"/> is an open string
/// (<c>'safe'</c> | <c>'uncertain'</c> are documented) rather than the typed
/// <see cref="EzClassify.Confidence"/>; the repair confidence vocabulary
/// is a distinct, deliberately open set on the wire.
/// </summary>
/// <param name="RuleId">The id of the repair rule that fired.</param>
/// <param name="Description">What the repair does, as the scheme describes it.</param>
/// <param name="Confidence">The confidence the scheme declares for the repair rule.</param>
/// <param name="Before">The text before the repair.</param>
/// <param name="After">The text after the repair.</param>
/// <param name="Cosmetic">True when the repair changed only formatting and left the assertions as they were.</param>
public record TraceRepair(
    string RuleId,
    string Description,
    string Confidence,
    string Before,
    string After,
    bool? Cosmetic = null
);

/// <summary>
/// One inference the engine drew: the rule that fired, the concept it added, and
/// the human-readable reason. Mirrors <c>ExplanationTrace.inferences</c> items.
/// </summary>
/// <param name="RuleId">The id of the inference rule that fired.</param>
/// <param name="ConceptId">The concept the rule added.</param>
/// <param name="Explanation">Why the rule added the concept, in words.</param>
/// <param name="ConceptName">Resolved operational display for <c>conceptId</c> (the concept's rendered marking view), so a "why" breakdown UI can show the inferred marking rather than the raw id. <c>conceptId</c> stays as the machine key.</param>
public record TraceInference(
    string RuleId,
    string ConceptId,
    string Explanation,
    string? ConceptName = null
);

/// <summary>
/// A validation problem recorded in an explanation trace. The wire deliberately
/// trims this to <c>constraintId</c> + <c>message</c> here; it is NOT the full
/// <see cref="ValidationError"/> shape, which carries severity, category, and
/// affected concepts.
/// </summary>
/// <param name="ConstraintId">The id of the constraint that failed.</param>
/// <param name="Message">What is wrong, in words.</param>
public record TraceValidationIssue(
    string ConstraintId,
    string Message
);

/// <summary>
/// What explain answers: the step-by-step trace of how the engine settled one
/// marking. It carries the input and scheme, each stage entry, the repairs and
/// inferences that fired, the trimmed validation issues, the final assertions,
/// and the rendered marking. Explain takes what process takes, and no other
/// result carries the trace.
/// </summary>
/// <param name="Input">The marking text the explanation was asked for, as given.</param>
/// <param name="SchemeId">The id of the scheme the marking was read against.</param>
/// <param name="Rendered">The settled marking, written in the output format.</param>
/// <param name="Entries">Every step the pipeline took, in the order it took them.</param>
/// <param name="Repairs">Each repair rule that rewrote the input, in the order applied.</param>
/// <param name="Inferences">Each concept an inference rule added, in the order added.</param>
/// <param name="ValidationErrors">The constraints the settled marking violates.</param>
/// <param name="FinalAssertions">The assertions the pipeline settled on, listed in print order as a validate result's <c>assertions</c> are.</param>
public record ExplanationTrace(
    string Input,
    string SchemeId,
    string Rendered,
    IReadOnlyList<TraceEntry> Entries,
    IReadOnlyList<TraceRepair> Repairs,
    IReadOnlyList<TraceInference> Inferences,
    IReadOnlyList<TraceValidationIssue> ValidationErrors,
    IReadOnlyList<Assertion> FinalAssertions
);

/// <summary>
/// Per-rule breakdown in a guide evaluation result: the qualified rule, the
/// outcome it contributed, whether it was a computed (auto-activated) rule, and
/// whether another rule overrode it.
///
/// <see cref="Outcome"/> stays opaque JSON, as it does on <see cref="GuideRule"/>:
/// it is the rendered marking or concept references the guide declares, which
/// the server has already applied.
/// </summary>
/// <param name="Rule">Qualified rule whose outcome was evaluated.</param>
/// <param name="Outcome">Rule outcome evaluated by the guide engine.</param>
/// <param name="IsComputed">Whether the rule was activated from other guide facts.</param>
/// <param name="WasOverridden">Whether an active rule replaced this rule's outcome.</param>
/// <param name="OverriddenBy">Qualified overriding rule when this outcome was replaced.</param>
public record GuideBreakdownEntry(
    RuleReference Rule,
    JsonElement Outcome,
    bool IsComputed,
    bool WasOverridden,
    RuleReference? OverriddenBy = null
);

/// <summary>
/// Whether a parent marking covers its children, checked category by category.
/// A parent can fail in two directions, and each has its own list:
/// <c>Missing</c> is what the children need that the parent does not carry, and
/// <c>Excess</c> is what the parent carries that the children do not account for.
/// Which list a concept is in says whether to add it or remove it.
/// <c>Refusals</c> is null unless a rollup refused to combine the children.
/// </summary>
/// <param name="Covered">True when nothing is missing, nothing is in excess, and no rollup refused.</param>
/// <param name="Missing">Concepts the children need that the parent does not carry.</param>
/// <param name="MissingLabels">Display labels for <c>Missing</c>, deduplicated and not aligned with it by position.</param>
/// <param name="Excess">Concepts the parent carries that the children do not account for.</param>
/// <param name="ExcessLabels">Display labels for <c>Excess</c>, deduplicated and not aligned with it by position.</param>
/// <param name="Rendered">The children's aggregate, rendered in the requested format.</param>
/// <param name="Refusals">The errors of each rollup that refused to combine the children, or null when none did.</param>
public record CoverageReport(
    bool Covered,
    IReadOnlyList<Assertion> Missing,
    IReadOnlyList<string> MissingLabels,
    IReadOnlyList<Assertion> Excess,
    IReadOnlyList<string> ExcessLabels,
    string Rendered,
    IReadOnlyList<ValidationError>? Refusals = null
);

/// <summary>A concept's usage: definition, guidance, applicable rules, and pre-formatted tooltip lines.</summary>
/// <param name="ConceptId">The id of the concept described.</param>
/// <param name="CategoryId">The category the concept belongs to.</param>
/// <param name="Name">The concept's display name.</param>
/// <param name="Lines">Pre-formatted lines for tooltip display (header, description, guidance, rules).</param>
/// <param name="Definition">What the concept means, as the scheme defines it.</param>
/// <param name="Guidance">How to apply the concept, as the scheme advises.</param>
/// <param name="Rules">The scheme's rules that involve the concept, each as a sentence.</param>
public record ConceptDescription(
    string ConceptId,
    string CategoryId,
    string Name,
    IReadOnlyList<string> Lines,
    string? Definition = null,
    string? Guidance = null,
    IReadOnlyList<string>? Rules = null
);

/// <summary>Visible/total pair for a filter bucket.</summary>
/// <param name="Visible">Items that matched the query.</param>
/// <param name="Total">Items in every category.</param>
public record FilterCount(
    int Visible,
    int Total
);

/// <summary>
/// Substring-filter result over concepts or guide rules: which ids matched,
/// per-category counts, and the global visible/total tally.
/// </summary>
/// <param name="VisibleIds">IDs of concepts or rules that matched the query.</param>
/// <param name="CategoryCounts">Per-category counts.</param>
/// <param name="Total">Aggregate counts across all categories.</param>
public record FilterResult(
    IReadOnlyList<string> VisibleIds,
    IReadOnlyDictionary<string, FilterCount> CategoryCounts,
    FilterCount Total
);

/// <summary>A regulatory or handling notice attached to a concept, collected for a set of assertions.</summary>
/// <param name="ConceptId">The concept the notice is attached to.</param>
/// <param name="ConceptName">The concept's display name, for showing beside the notice.</param>
/// <param name="Type">What kind of notice this is.</param>
/// <param name="Text">The notice text the marking requires.</param>
/// <param name="Required">Whether the notice must be shown. A host surfaces a required notice before the marking is accepted.</param>
/// <param name="Views">The notice's own <c>ConceptNotice.views</c>, carried through unchanged.</param>
public record Notice(
    string ConceptId,
    string ConceptName,
    string Type,
    string Text,
    bool Required,
    IReadOnlyDictionary<string, string>? Views = null
);

/// <summary>Server-side description of a single guide rule.</summary>
/// <param name="Rule">Qualified rule being described.</param>
/// <param name="Guide">Guide artifact that owns the rule.</param>
/// <param name="Name">Human-readable rule name.</param>
/// <param name="OutcomeText">Human-readable outcome expression.</param>
/// <param name="IsComputed">Whether the rule is computed from other guide facts.</param>
/// <param name="DependsOn">Qualified rules referenced by this rule's activation block.</param>
/// <param name="Overrides">Qualified rules this rule overrides when it fires.</param>
/// <param name="Lines">Flattened multi-line description matching <c>ConceptDescription.lines</c>.</param>
/// <param name="Description">Optional policy-authored description.</param>
/// <param name="WhenText">Human-readable activation expression.</param>
public record RuleDescription(
    RuleReference Rule,
    ArtifactReference Guide,
    string Name,
    string OutcomeText,
    bool IsComputed,
    IReadOnlyList<RuleReference> DependsOn,
    IReadOnlyList<RuleReference> Overrides,
    IReadOnlyList<string> Lines,
    string? Description = null,
    string? WhenText = null
);

/// <summary>Server-side description of a single access rule.</summary>
/// <param name="Rule">Qualified rule being described.</param>
/// <param name="Access">Access artifact that owns the rule.</param>
/// <param name="Scheme">Scheme artifact whose assertions the rule evaluates.</param>
/// <param name="Name">Human-readable rule name.</param>
/// <param name="RequirementText">Human-readable requirement expression.</param>
/// <param name="AttributesNeeded">Subject or context paths required to evaluate the rule.</param>
/// <param name="DependsOn">Qualified rules this rule depends on.</param>
/// <param name="Overrides">Qualified rules this rule overrides.</param>
/// <param name="Lines">Line-oriented rendering of the description.</param>
/// <param name="Description">Optional policy-authored description.</param>
/// <param name="WhenText">Human-readable activation expression.</param>
public record AccessRuleDescription(
    RuleReference Rule,
    ArtifactReference Access,
    ArtifactReference Scheme,
    string Name,
    string RequirementText,
    IReadOnlyList<string> AttributesNeeded,
    IReadOnlyList<RuleReference> DependsOn,
    IReadOnlyList<RuleReference> Overrides,
    IReadOnlyList<string> Lines,
    string? Description = null,
    string? WhenText = null
);

/// <summary>
/// Aggregated access requirements for a marking: every rule that would fire
/// plus the flat union of subject attributes the caller has to provide.
/// </summary>
/// <param name="Assertions">Normalized effective target assertions, listed in print order: the order <c>marking</c> places each concept; a concept it leaves out appears where it would print.</param>
/// <param name="AssertionDisplays">Resolved operational display (rendered marking view) for each entry in <c>assertions</c>, index-aligned. A consumer that instantiates <c>requirementText</c> against the target assertions substitutes this rather than the raw concept id, so the requirement renders the concept's marking token instead of its internal id. <c>assertions</c> keeps the machine <c>conceptId</c> keys.</param>
/// <param name="Access">Access artifact whose policy was described.</param>
/// <param name="Scheme">Scheme artifact used to normalize the target.</param>
/// <param name="AppliedRules">Qualified rules activated by the trusted target.</param>
/// <param name="Rules">Per-rule immutable-policy descriptions.</param>
/// <param name="CombinedRequirementText">Combined human-readable requirement expression.</param>
/// <param name="CombinedAttributesNeeded">Union of subject and context paths needed to evaluate the target.</param>
/// <param name="Lines">Line-oriented rendering of the combined description.</param>
/// <param name="Marking">Normalized marking when the caller supplied or derived one.</param>
public record AccessRequirementsDescription(
    IReadOnlyList<Assertion> Assertions,
    IReadOnlyList<string> AssertionDisplays,
    ArtifactReference Access,
    ArtifactReference Scheme,
    IReadOnlyList<RuleReference> AppliedRules,
    IReadOnlyList<AccessRuleDescription> Rules,
    string CombinedRequirementText,
    IReadOnlyList<string> CombinedAttributesNeeded,
    IReadOnlyList<string> Lines,
    string? Marking = null
);

/// <summary>One category of a scheme view, with its concepts in declared order.</summary>
/// <param name="Id">The category id.</param>
/// <param name="Label">The category's display label.</param>
/// <param name="Items">The category's concepts, in declared order.</param>
public record SchemeViewCategory(
    string Id,
    string Label,
    IReadOnlyList<ConceptView> Items
);

/// <summary>
/// Render-ready tree of a scheme: its categories of concepts, in declared
/// order, plus the concepts that declare no category.
/// </summary>
/// <param name="Id">The scheme id.</param>
/// <param name="Name">The scheme's display name.</param>
/// <param name="Description">The scheme's description. Empty when the scheme declares none.</param>
/// <param name="Categories">The scheme's categories in declared order, each with the concepts that belong to it. A category with no concepts is left out.</param>
/// <param name="Uncategorized">Concepts that belong to none of the scheme's loaded categories. Usually empty.</param>
/// <param name="Vocabulary">Display-term overrides the scheme declares, unresolved.</param>
public record SchemeView(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<SchemeViewCategory> Categories,
    IReadOnlyList<ConceptView> Uncategorized,
    IReadOnlyDictionary<string, Term>? Vocabulary = null
);

/// <summary>One category of a guide view, with its rules in declared order.</summary>
/// <param name="Id">The category id, or <c>_compound</c> for the compound rules.</param>
/// <param name="Label">The category's display label.</param>
/// <param name="Items">The category's rules, in declared order.</param>
public record GuideViewCategory(
    string Id,
    string Label,
    IReadOnlyList<GuideRuleView> Items
);

/// <summary>
/// Render-ready Guidebook tree of a guide: its categories of rules, in declared
/// order, plus the rules that declare no category.
/// </summary>
/// <param name="Id">The guide id.</param>
/// <param name="Name">The guide's display name.</param>
/// <param name="Description">The guide's description. Empty when the guide declares none.</param>
/// <param name="Categories">The guide's categories in declared order, each with its checkable rules, followed by the <c>_compound</c> category that holds every rule with a <c>when</c> condition. A category with no rules is left out.</param>
/// <param name="Uncategorized">Checkable rules that name no category, or a category the guide does not declare.</param>
/// <param name="Vocabulary">Display-term overrides the guide declares, unresolved.</param>
public record GuideView(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<GuideViewCategory> Categories,
    IReadOnlyList<GuideRuleView> Uncategorized,
    IReadOnlyDictionary<string, Term>? Vocabulary = null
);

/// <summary>
/// One rule of a guide as the Guidebook shows it: its name and outcome in
/// words, what a text scan matches for it, and the rules it depends on and
/// overrides, each with the display names index-aligned to the ids.
/// </summary>
/// <param name="Id">The rule id.</param>
/// <param name="Name">The rule's display name.</param>
/// <param name="OutcomeText">The rule's outcome for display: its rendered marking, or its concept ids listed in order when it has none. Empty when the outcome carries neither.</param>
/// <param name="IsComputed">True when the rule has a <c>when</c> condition and is activated by it.</param>
/// <param name="Keywords">The keywords a text scan matches for this rule.</param>
/// <param name="MatcherLabels">Labels like "IPv4 address", "CIDR block", already resolved via getMatcher.</param>
/// <param name="DependsOn">Rule ids this compound rule references.</param>
/// <param name="DependsOnNames">Resolved display names for each id in <c>dependsOn</c>, index-aligned, so a "depends on" line reads the rule's name rather than its raw id. <c>dependsOn</c> stays as the machine keys.</param>
/// <param name="Overrides">Rule ids this compound rule overrides when it fires.</param>
/// <param name="OverrideNames">Resolved display names for each id in <c>overrides</c>, index-aligned, so the Guidebook "Overrides:" line reads rule names rather than raw ids, matching the sibling "When:" line which already resolves display. <c>overrides</c> stays as the machine keys.</param>
/// <param name="Description">The rule's description, when it declares one.</param>
/// <param name="WhenText">The rule's <c>when</c> condition in words. Set only on a computed rule.</param>
/// <param name="Rationale">Optional rationale text from the rule definition.</param>
public record GuideRuleView(
    string Id,
    string Name,
    string OutcomeText,
    bool IsComputed,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> MatcherLabels,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> DependsOnNames,
    IReadOnlyList<string> Overrides,
    IReadOnlyList<string> OverrideNames,
    string? Description = null,
    string? WhenText = null,
    string? Rationale = null
);

/// <summary>
/// How an audit judged one authored marking, whole or part, separately from
/// coverage. It carries the marking's facts and how far recognition got with
/// it, so a caller that rolls markings up, checks access, or offers a
/// correction works from the audit instead of reading each marking again.
/// </summary>
/// <param name="Assertions">The facts the marking states, settled as a validate result's are, in the role the marking has in the document.</param>
/// <param name="Recognition">What recognition made of the marking, as on <see cref="ValidateResult.Recognition"/>.</param>
/// <param name="Valid">Whether the marking stands once settled, as <see cref="ValidateResult.Valid"/>.</param>
/// <param name="InputWasValid">Whether the marking was right as the author wrote it, as <see cref="ValidateResult.InputWasValid"/>. This is the question an audit asks.</param>
/// <param name="Errors">What the scheme's rules find wrong with the settled marking.</param>
/// <param name="ProposedFixes">The ways to make the marking valid, each a whole marking the engine has proven.</param>
/// <param name="AppliedFixes">Every change the engine made while settling the marking; empty when it made none, so a false <paramref name="InputWasValid"/> is always explained here or in <paramref name="Errors"/>.</param>
/// <param name="Rendered">The settled marking, written in its surfaces' defaults for the role the marking has in the document.</param>
public record MarkingValidation(
    IReadOnlyList<Assertion> Assertions,
    RecognitionResult Recognition,
    bool Valid,
    bool InputWasValid,
    IReadOnlyList<ValidationError> Errors,
    IReadOnlyList<Fix> ProposedFixes,
    IReadOnlyList<Change> AppliedFixes,
    string Rendered
);

/// <summary>A run of document text that falls outside every detected marking.</summary>
/// <param name="Start">Zero-based inclusive offset in the accepted document.</param>
/// <param name="End">Zero-based exclusive offset in the accepted document.</param>
/// <param name="OffsetEncoding">Frozen byte-offset encoding.</param>
/// <param name="Text">Authored text, omitted outside the full response profile.</param>
public record UnmarkedSpan(
    int Start,
    int End,
    string OffsetEncoding,
    string? Text = null
);

/// <summary>A document-wide marking detected during an audit, with its byte span.</summary>
/// <param name="Marking">The whole marking as the document writes it.</param>
/// <param name="Start">Zero-based inclusive UTF-8 byte offset.</param>
/// <param name="End">Zero-based exclusive UTF-8 byte offset.</param>
/// <param name="OffsetEncoding">Frozen byte-offset encoding.</param>
public record AuditWhole(
    string Marking,
    int Start,
    int End,
    string OffsetEncoding
);

/// <summary>Per-part audit result with optional profile-projected text and coverage/validity verdicts.</summary>
/// <param name="Marking">The part marking as the document writes it.</param>
/// <param name="Text">The text the part marking governs. Omitted outside the full response profile.</param>
/// <param name="Start">Zero-based inclusive UTF-8 byte offset.</param>
/// <param name="End">Zero-based exclusive UTF-8 byte offset.</param>
/// <param name="OffsetEncoding">Frozen byte-offset encoding.</param>
/// <param name="ScopeId">The scope id the part is evaluated under, <c>part-</c> followed by its one-based position in the document. Guide results for the part carry the same id.</param>
/// <param name="Validation">How the part marking was judged, with the facts it states.</param>
public record AuditPart(
    string Marking,
    string? Text,
    int Start,
    int End,
    string OffsetEncoding,
    string ScopeId,
    MarkingValidation Validation
);

/// <summary>Whether the loaded scheme requires this document to carry a whole marking and part markings.</summary>
/// <param name="Whole">Whether an authored document-wide marking is required.</param>
/// <param name="Part">Whether authored part markings are required for document text.</param>
public record DocumentMarkingRequirements(
    bool Whole,
    bool Part
);

/// <summary>A singular/plural display-word pair.</summary>
/// <param name="Singular">The word for one, such as <c>rule</c>.</param>
/// <param name="Plural">The word for more than one, such as <c>rules</c>.</param>
public record Term(
    string Singular,
    string Plural
);

/// <summary>
/// Composed document audit: whole-marking adequacy, per-part coverage, unmarked
/// content, and an aggregate verdict. Returned by the context document-audit op.
/// </summary>
/// <param name="MarkingRequirements">Evaluated canonical marking requirements, independent of discovered evidence.</param>
/// <param name="Whole">First authored document-wide marking, or null when absent.</param>
/// <param name="Wholes">Every authored document-wide marking occurrence.</param>
/// <param name="WholeValidation">How the first authored whole was judged, as a whole that must cover the authored parts that read as markings, with the facts it states. Null when the document has no authored whole.</param>
/// <param name="Parts">Authored part markings and their governed regions.</param>
/// <param name="WholeCoversParts">Authored whole coverage of authored parts.</param>
/// <param name="UnmarkedSpans">Authored ranges not governed by a marking.</param>
/// <param name="ProceduralFlags">Stable authored-procedure findings.</param>
/// <param name="OverallVerdict">Verdict derived only from authored structural dimensions.</param>
/// <param name="DocumentDigest">SHA-256 digest of the exact accepted UTF-8 document bytes.</param>
/// <param name="Limitations">Document-level assessment limitations.</param>
/// <param name="Vocabulary">Scheme-provided display vocabulary.</param>
/// <param name="GuideAssessment">Guide compilation dimension, or null when no guide applies.</param>
public record DocumentAuditReport(
    DocumentMarkingRequirements MarkingRequirements,
    string? Whole,
    IReadOnlyList<AuditWhole> Wholes,
    MarkingValidation? WholeValidation,
    IReadOnlyList<AuditPart> Parts,
    CoverageReport? WholeCoversParts,
    IReadOnlyList<UnmarkedSpan> UnmarkedSpans,
    IReadOnlyList<string> ProceduralFlags,
    AuditVerdict OverallVerdict,
    string DocumentDigest,
    IReadOnlyList<AssessmentLimitation> Limitations,
    IReadOnlyDictionary<string, Term>? Vocabulary = null,
    DocumentGuideAssessment? GuideAssessment = null
);

// ---------------------------------------------------------------------------
// Scheme resource models.
//
// Read-only projections of a composed scheme: summary metadata, the concept and
// category lists, the render formats, the parse grammar, and the label presets.
// They are what a remote adapter needs to render and explain markings without
// holding the scheme document itself.
//
// These records serve the Context-bound scheme resources under
// /v1/ctx/{ctxId}/{version}/scheme/*. The Context selects the composed scheme;
// all but the label presets take an optional scheme query that identifies a
// source or target scheme within a crosswalk-bearing Context.

/// <summary>
/// Resolved display label for an artifact's own reference surface: the noun a
/// scheme's Handbook wears in a UI. Always fully populated on the wire, falling
/// back to the engine's generic noun when the scheme declares no override.
/// </summary>
/// <param name="Singular">The noun for one, such as <c>Handbook</c>.</param>
/// <param name="Plural">The noun for more than one, such as <c>Handbooks</c>.</param>
public record ReferenceSurfaceLabel(
    string Singular,
    string Plural
);

/// <summary>
/// Render format ids grouped by the structural role they mark. For a scheme
/// whose formats are its outputs, these are the lists the scanner reads a
/// marking's role from. For a scheme that declares surfaces, each role lists
/// the forms of the views that state that role, for a caller that picks a
/// format per role.
/// </summary>
/// <param name="Whole">Format ids a whole marking is read in.</param>
/// <param name="Part">Format ids a part marking is read in.</param>
public record SchemeContextFormats(
    IReadOnlyList<string> Whole,
    IReadOnlyList<string> Part
);

/// <summary>
/// One entry in a scheme's ordered display-color rule list. The last rule may
/// omit <see cref="When"/> to act as the unconditional fallback.
///
/// <see cref="When"/> stays opaque JSON for the same reason
/// <see cref="GuideRule.When"/> does: it carries the recursive
/// assertion-match DSL, which this SDK passes through rather than over-models.
/// </summary>
/// <param name="Color">Complete background/text pair to use when the condition matches.</param>
/// <param name="When">Assertion-set condition. Omit only on the final rule to create the standard unconditional/anything-else fallback.</param>
public record MarkingColorRule(
    ConceptColor Color,
    JsonElement? When = null
);

/// <summary>
/// High-level metadata for a composed scheme: identity, presentation
/// configuration, and the render defaults a remote caller cannot derive from
/// the other sub-resources.
///
/// <see cref="RoleAware"/> is null when the scheme leaves it unset.
/// <see cref="EmbeddedMarking"/> is the list of ways a marking may appear
/// embedded in free text, carried as opaque JSON; a null one is how a client
/// tells that inline marking is off.
///
/// <see cref="DefaultRenderFormat"/> is the output a call that names none is
/// written in. A scheme that declares surfaces names its outputs
/// <c>surface/view</c> and writes a whole and a part in their own defaults:
/// <see cref="DefaultWholeRenderFormat"/> and
/// <see cref="DefaultPartRenderFormat"/> give those addresses, and are null
/// for a scheme whose formats are its outputs, which has the one default.
/// </summary>
/// <param name="Id">Scheme identifier from the scheme metadata.</param>
/// <param name="Version">Scheme semantic version.</param>
/// <param name="Name">Display name from the scheme metadata.</param>
/// <param name="Description">Free-text description from the scheme metadata.</param>
/// <param name="RoleAware">Whether the scheme uses role-aware processing. Omitted when unset.</param>
/// <param name="ReferenceSurfaceLabel">Display label for this scheme's own reference component, the Handbook: the scheme's override or the generic "Handbook" / "Handbooks" when it declares none. Always populated.</param>
/// <param name="EmbeddedMarking">Ways a marking may appear embedded in free text. Omitted when the scheme declares none, which is how a client tells that inline marking is off.</param>
/// <param name="DefaultRenderFormat">Render format applied when a call names none. A remote adapter has no way to derive this from the formats sub-resource: format declaration order is not default order, so a client that guesses the first entry renders in the wrong format whenever the two differ.</param>
/// <param name="Definition">Human-readable definition of the scheme, for tooltips and about panels.</param>
/// <param name="Guidance">Usage guidance for the scheme, shown in reference views.</param>
/// <param name="ColorFrom">Single-category scheme-default color source, when category mode is used.</param>
/// <param name="ColorRules">Ordered rule-driven color outcomes, when rule mode is used.</param>
/// <param name="Vocabulary">Scheme-authored display vocabulary.</param>
/// <param name="ContextFormats">Render format IDs grouped by role. For a scheme without <c>render.surfaces</c>, the lists <c>role_detection</c> gives, which the scanner reads a marking's role from. For a scheme with them, the scanner takes the role from the view a marking was read as, and these lists are derived from the views for a caller that picks a format per role: each role lists the forms of the views that state it, <c>format</c>, case formats and <c>also_read_in</c> in that order, surfaces and views in declared order and each format once.</param>
/// <param name="DefaultScope">Default scope id applied when a marking asserts no scope explicitly. Present exactly when the scheme declares scopes. Pairs with the per-category <c>scopes</c> lists on the categories sub-resource so a remote adapter can reconstruct which categories a scope-only assertion set targets: the metadata the assertion-set aggregate path needs to render scoped categories that the whole-marking format would otherwise drop.</param>
/// <param name="ScopeLabel">Display label for the scope selector shown in UIs. Omitted when the scheme declares none. Carried here so <c>getSchemeMeta</c> answers the same for a remote adapter as it does for a local one; without it the selector is labeled from the scheme document in one mode and left unlabeled in the other.</param>
/// <param name="DefaultPartRenderFormat">Present only for a scheme that declares <c>render.surfaces</c>: the output address a part marking is written in by default. It is the <c>surface/view</c> of the default scope's surface's default for a part, from that surface's <c>default</c>, and does not depend on <c>contextFormats</c>, which lists the formats each role is read in.</param>
/// <param name="DefaultWholeRenderFormat">Present only for a scheme that declares <c>render.surfaces</c>: the output address a whole marking is written in by default, the default scope's surface's default for a whole, resolved the same way <c>defaultPartRenderFormat</c> is for a part.</param>
public record SchemeSummary(
    string Id,
    string Version,
    string Name,
    string Description,
    bool? RoleAware,
    ReferenceSurfaceLabel ReferenceSurfaceLabel,
    JsonElement? EmbeddedMarking,
    string? DefaultRenderFormat,
    string? Definition = null,
    string? Guidance = null,
    string? ColorFrom = null,
    IReadOnlyList<MarkingColorRule>? ColorRules = null,
    IReadOnlyDictionary<string, Term>? Vocabulary = null,
    SchemeContextFormats? ContextFormats = null,
    string? DefaultScope = null,
    string? ScopeLabel = null,
    string? DefaultPartRenderFormat = null,
    string? DefaultWholeRenderFormat = null
);

/// <summary>
/// A regulatory or handling notice a concept carries, as the concept view
/// lists it: what has to be said, what kind of notice it is, and whether
/// showing it is mandatory. Where the text appears is the host's to decide.
/// </summary>
/// <param name="Type">What kind of notice this is.</param>
/// <param name="Required">Whether this notice MUST be displayed (regulatory requirement)</param>
/// <param name="Text">The notice text the marking requires.</param>
/// <param name="Condition">Only show when this concept is also present</param>
/// <param name="Views">The value this notice is registered under, keyed by view.</param>
public record ConceptNotice(
    string Type,
    bool Required,
    string Text,
    string? Condition = null,
    IReadOnlyDictionary<string, string>? Views = null
);

/// <summary>
/// One concept of a scheme, resolved for display: identity, hierarchy,
/// position, the spellings it has in each view, and the documentation a
/// reference surface shows beside it. The same shape a local engine's scheme
/// view carries.
///
/// <see cref="Views"/> maps a view name to the spelling the concept has in
/// it; the map always holds at least <c>default</c>. <see cref="TextInput"/>
/// stays opaque JSON: it configures a free-text input, and a remote adapter
/// does not need it decoded.
///
/// Everything past <see cref="Position"/> is optional on the wire. A concept
/// that declares no rank, no members, no scopes, and no deprecation leaves
/// those null rather than sending empty values.
/// </summary>
/// <param name="Id">Unique concept identifier.</param>
/// <param name="Name">Human-readable display name.</param>
/// <param name="Views">Display representations keyed by view name; includes at least <c>'default'</c>.</param>
/// <param name="Aliases">Alternative strings that parse to this concept.</param>
/// <param name="Categories">Category IDs this concept belongs to.</param>
/// <param name="Parents">Broader concept IDs forming the hierarchy chain.</param>
/// <param name="Position">Resolved rendering position within a category (lower = earlier).</param>
/// <param name="Color">Portable scheme-default display color pair, present only on concepts in the scheme's category-based <c>color_from</c> mode. Absent otherwise.</param>
/// <param name="Rank">Resolved severity rank for max/min rollup aggregation (higher = more severe).</param>
/// <param name="Tier">Tier label: marks this concept as a partial marking.</param>
/// <param name="Members">For group concepts: the member concept IDs.</param>
/// <param name="Scopes">Scope IDs this concept is visible in.</param>
/// <param name="Deprecated">Whether this concept is deprecated.</param>
/// <param name="DeprecatedMessage">Human-readable deprecation message.</param>
/// <param name="DeprecatedReplacement">Concept ID that should be used instead of this deprecated concept.</param>
/// <param name="Definition">Definition text from the concept's documentation block.</param>
/// <param name="Citation">Where this concept came from: the provenance text the scheme records against it.</param>
/// <param name="Guidance">Usage guidance from the concept's documentation block.</param>
/// <param name="Notices">Regulatory or policy notices shown with this concept.</param>
/// <param name="RulesReferencing">Human-readable summary of rules that reference this concept. Set by <c>buildSchemeView</c>; absent from <c>getConceptsInCategory</c>.</param>
/// <param name="TextInput">Text-input configuration for free-text concepts.</param>
public record ConceptView(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Views,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Parents,
    double Position,
    ConceptColor? Color = null,
    double? Rank = null,
    string? Tier = null,
    IReadOnlyList<string>? Members = null,
    IReadOnlyList<string>? Scopes = null,
    bool? Deprecated = null,
    string? DeprecatedMessage = null,
    string? DeprecatedReplacement = null,
    string? Definition = null,
    string? Citation = null,
    string? Guidance = null,
    IReadOnlyList<ConceptNotice>? Notices = null,
    IReadOnlyList<string>? RulesReferencing = null,
    JsonElement? TextInput = null
);

/// <summary>
/// One category of a scheme, resolved for display and selection: its label,
/// the scopes that offer it, and how its concepts roll up and sort. The same
/// shape a local engine's scheme view carries.
///
/// <see cref="Sort"/> and <see cref="DefaultConcept"/> stay opaque JSON. Sort
/// is a single key or an ordered list of keys; the default is either an
/// unconditional concept identifier or a conditional object holding its
/// concept and when expression. <see cref="Rollup"/> is the merge operator
/// applied across aggregated inputs, kept as the token the server sent.
/// Position overrides apply only to this category when position sorting is
/// selected.
/// </summary>
/// <param name="Id">Unique category identifier.</param>
/// <param name="Label">Resolved human-readable label (always a plain string).</param>
/// <param name="Rollup">Rollup operator used when aggregating multiple markings.</param>
/// <param name="Required">Whether at least one concept is required for a valid marking.</param>
/// <param name="Single">Whether only one concept can be selected in this category.</param>
/// <param name="Sort">Sort order for concepts within this category.</param>
/// <param name="PositionOverrides">Concept positions used only by this category's position sort.</param>
/// <param name="Scopes">Scope IDs this category is visible in.</param>
/// <param name="Position">Declared rendering position (lower = earlier). Not resolved from array order.</param>
/// <param name="HierarchyRoot">When true, the category grades its concepts by severity <c>rank</c>, and a coverage comparison counts a whole-marking concept here as covering a part's concept of equal or lower rank. It does not change rendering.</param>
/// <param name="DefaultConcept">Concept ID to assert automatically when no selection exists in this category. With <c>single: true</c>, this provides a default level. Mirrors the scheme's <c>default_concept</c> for the category.</param>
public record CategoryView(
    string Id,
    string Label,
    string Rollup,
    bool? Required = null,
    bool? Single = null,
    JsonElement? Sort = null,
    IReadOnlyDictionary<string, double>? PositionOverrides = null,
    IReadOnlyList<string>? Scopes = null,
    double? Position = null,
    bool? HierarchyRoot = null,
    JsonElement? DefaultConcept = null
);

/// <summary>
/// One output's identity and layout: the part of a marking it writes, how it
/// lays the fields out, and the text it puts between and around categories.
/// The authored field tree and recognition rules stay on the server.
///
/// For a scheme whose formats are its outputs, each entry is a format. For a
/// scheme that declares surfaces, each entry is a view, addressed
/// <c>surface/view</c>, laid out as that view's base format writes it, and it
/// says which roles it is the default for and which role it is a view of.
/// </summary>
/// <param name="Id">The output this entry describes, named by callers choosing an output or input format: a format id, or a <c>surface/view</c> address for a scheme that declares surfaces.</param>
/// <param name="Surface">The part of a marking this format writes: a scheme scope id.</param>
/// <param name="Mode">Whether the fields run together or each goes on its own line.</param>
/// <param name="CategoryOrder">The categories this format writes, in the order it writes them.</param>
/// <param name="View">Default concept view for fields that do not name one.</param>
/// <param name="Prefix">Written once before the nonempty whole, when it is written unconditionally.</param>
/// <param name="Suffix">Written once after the nonempty whole, when it is written unconditionally.</param>
/// <param name="Separator">What the format writes between two adjacent categories by default. Null for a format with no global separator, such as an XML-attribute encoding.</param>
/// <param name="CategorySeparators">What is written between two values of one category, keyed by category id.</param>
/// <param name="SeparatorBefore">What is written immediately before a category, keyed by category id.</param>
/// <param name="CategoryViews">The concept view a category is written in, where it differs from <c>View</c>.</param>
/// <param name="CategoryTiers">How a category's tiered concepts compose into one token, outermost tier first.</param>
/// <param name="CategoryEach">What is written around every single value of a category.</param>
/// <param name="Headers">Headings this format writes, paired with the lists they head.</param>
/// <param name="Syntax">The encoding, or null when the format is plain text.</param>
/// <param name="OutputView">The view part of <paramref name="Id"/>, unescaped, for a scheme that declares surfaces. This names an output, not a concept spelling; that is <paramref name="View"/>. Null for a scheme whose formats are its outputs.</param>
/// <param name="DefaultFor">For a scheme that declares surfaces, the roles this view is its surface's default for: a marking of each listed role is written in this view when a request names no output. Empty on the surface's other views. Null for a scheme whose formats are its outputs, which has the one default its summary reports.</param>
/// <param name="Role">For a scheme that declares surfaces, the role of the markings this view is a view of. Null for a view with no role, and for a scheme whose formats are its outputs.</param>
public record FormatView(
    string Id,
    string Surface,
    RenderFormatMode Mode,
    IReadOnlyList<string> CategoryOrder,
    string? View = null,
    string? Prefix = null,
    string? Suffix = null,
    string? Separator = null,
    IReadOnlyDictionary<string, string>? CategorySeparators = null,
    IReadOnlyDictionary<string, string>? SeparatorBefore = null,
    IReadOnlyDictionary<string, string>? CategoryViews = null,
    IReadOnlyDictionary<string, IReadOnlyList<TierComposition>>? CategoryTiers = null,
    IReadOnlyDictionary<string, EachAffix>? CategoryEach = null,
    IReadOnlyList<FormatHeaderAssociation>? Headers = null,
    FormatSyntax? Syntax = null,
    string? OutputView = null,
    IReadOnlyList<StructuralRole>? DefaultFor = null,
    StructuralRole? Role = null
);

/// <summary>How one tier of a category's concepts attaches to the tier above it.</summary>
/// <param name="Tier">The tier this step writes.</param>
/// <param name="AttachedBy">The text that joins a concept of this tier to the one it belongs to.</param>
/// <param name="View">The concept view this tier is written in, when it differs.</param>
/// <param name="HeadView">The view the owning concept is written in when this tier attaches to it.</param>
public record TierComposition(
    string Tier,
    string AttachedBy,
    string? View = null,
    string? HeadView = null
);

/// <summary>What is written around each single value of a category.</summary>
/// <param name="Prefix">Written before each value.</param>
/// <param name="Suffix">Written after each value.</param>
public record EachAffix(
    string? Prefix = null,
    string? Suffix = null
);

/// <summary>A heading concept a format writes, paired with the list it heads.</summary>
/// <param name="Concept">The heading concept.</param>
/// <param name="HeaderCategory">The category the heading concept belongs to.</param>
/// <param name="Category">The category whose values the heading introduces.</param>
/// <param name="Position">Whether the heading comes before or after the list.</param>
public record FormatHeaderAssociation(
    string Concept,
    string HeaderCategory,
    string Category,
    FormatHeaderPosition Position
);

/// <summary>A format's encoding.</summary>
/// <param name="Type">The framing this format writes its text in.</param>
public record FormatSyntax(
    FormatSyntaxType Type
);

/// <summary>The parse grammar a scheme's marking strings follow.</summary>
/// <param name="CaseSensitive">Whether the vocabulary is matched case-sensitively.</param>
/// <param name="StripWhitespace">Whether surrounding whitespace is ignored when a spelling is compared.</param>
public record SchemeGrammar(
    bool CaseSensitive,
    bool StripWhitespace
);

/// <summary>One scope a scheme declares, as the scopes resource lists it.</summary>
/// <param name="Id">Scope identifier, referenced by <c>scopes</c> on concepts and categories.</param>
/// <param name="Label">Display label for the scope selector.</param>
public record SchemeScope(
    string Id,
    string Label
);

/// <summary>
/// One entry in a scheme's public label preset list. Name and scope only: the
/// preset's marking string and tag list stay server-side, so a preset is
/// expanded by calling the apply-preset operation rather than by reading it.
/// </summary>
/// <param name="Id">Preset id, which <c>applyPreset</c> takes.</param>
/// <param name="Label">Display label for the preset.</param>
/// <param name="Description">Longer explanation of when to use the preset.</param>
/// <param name="Scope">Id of the scope the preset belongs to. A preset without one belongs to the scheme's <c>defaultScope</c>.</param>
public record LabelPresetView(
    string Id,
    string Label,
    string? Description = null,
    string? Scope = null
);
