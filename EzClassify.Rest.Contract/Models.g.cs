// Code generated from json-schemas@128fa98ab01e961965eee54c4249ef332176a52fa5a2edecba3e58a626e5836b by gen.mjs@5cf243baa1066cacdc9db5123130bfc7cc034a1a04ecc014f5cbca1e4f02e6ba. DO NOT EDIT.
// Re-run via: make regen
#nullable enable
#pragma warning disable CS1573

using System.Collections.Generic;
using System.Text.Json;

namespace EzClassify;

/// <summary>A concept asserted in a category during processing, with confidence and provenance metadata.</summary>
/// <param name="CategoryId">The id of the category the concept is asserted in.</param>
/// <param name="ConceptId">The id of the concept.</param>
/// <param name="Confidence">How the assertion was produced: <c>certain</c> when read or handed in as stated, <c>inferred</c> when an inference rule added it, or <c>repaired</c> when the engine added or rewrote it while repairing the marking. The engine does not assign <c>uncertain</c> itself.</param>
/// <param name="Source">Where this assertion came from. An open string: the engine labels the facts it adds, and a caller or a host may label the facts it hands in however it likes.</param>
/// <param name="ExpiresAt">Caller-supplied ISO 8601 expiry date; the engine does not evaluate it.</param>
/// <param name="Metadata">Caller-defined data attached to the assertion. The engine assigns it no meaning.</param>
/// <param name="Provenance">Source provenance for the concept that backed this assertion.</param>
/// <param name="ReplacedConceptId">The deprecated concept this assertion was modernized from.</param>
/// <param name="SourceRule">The id of the scheme rule that produced this assertion, when one did. Otherwise an engine-owned id beginning with <c>_</c>, set when the scheme's structure, not a rule, added the assertion. The engine writes two: <c>_tier_child_requires_parent</c> (a tier parent required by a stated child) and <c>_header_of_stated_member</c> (a header filled in for a stated member). A scheme rule id never begins with <c>_</c>.</param>
/// <param name="Value">The value of a concept that takes one (<c>text_input</c>).</param>
public record Assertion(
    string CategoryId,
    string ConceptId,
    Confidence Confidence,
    string Source,
    string? ExpiresAt = null,
    JsonElement? Metadata = null,
    SourceReference? Provenance = null,
    string? ReplacedConceptId = null,
    string? SourceRule = null,
    string? Value = null
);

/// <summary>A constraint violation detected during validation.</summary>
/// <param name="ConstraintId">The id of the constraint that was violated: a scheme rule's id, or an engine-reserved id beginning with <c>_</c>.</param>
/// <param name="Message">What is wrong, in words a person can act on.</param>
/// <param name="AffectedConcepts">Concept IDs involved in this validation failure.</param>
/// <param name="CategoryId">Category ID this error relates to (when determinable from the constraint).</param>
/// <param name="DidYouMean">The ids of the concepts the unread token was likely meant as. Never empty. They are ids for a program to act on, not spellings to show an author: <c>message</c> gives the same hint in words, naming the spellings.</param>
/// <param name="Severity">Severity: 'error' blocks validity, 'warning' is advisory. Default: 'error'.</param>
/// <param name="SuggestedFix">The <c>message</c> of the proposed fix that answers this error, such as "Add CTRL-A".</param>
public record ValidationError(
    string ConstraintId,
    string Message,
    IReadOnlyList<string>? AffectedConcepts = null,
    string? CategoryId = null,
    IReadOnlyList<string>? DidYouMean = null,
    Severity? Severity = null,
    string? SuggestedFix = null
);

/// <summary>A non-blocking informational signal: a deprecated concept, an authored warn rule, or an engine notice.</summary>
/// <param name="Kind">Warning kind: 'deprecation' for a deprecated concept, 'rule' for a scheme's authored <c>warn</c> rule, 'notice' for the engine's own notice.</param>
/// <param name="Message">Human-readable message</param>
/// <param name="CategoryId">Category this warning relates to (when applicable)</param>
/// <param name="ConceptId">Concept this warning relates to (when applicable). A rule warning names one when the rule's <c>when</c> names exactly one concept the marking holds.</param>
/// <param name="ConceptName">Resolved operational display for <c>conceptId</c> (the concept's rendered marking view), for a consumer that shows the warning next to the concept. <c>conceptId</c> stays as the machine key; this is the human-visible string.</param>
/// <param name="Replacement">When kind=deprecation, the suggested replacement concept id (if any)</param>
/// <param name="ReplacementName">When kind=deprecation, the resolved operational display for <c>replacement</c> (the successor concept's rendered marking view). <c>replacement</c> stays as the machine key; this is the human-visible string.</param>
/// <param name="RuleId">The scheme rule that gave a <c>rule</c> warning, or the engine-reserved id of a <c>notice</c>. Absent on a deprecation warning.</param>
public record Warning(
    WarningKind Kind,
    string Message,
    string? CategoryId = null,
    string? ConceptId = null,
    string? ConceptName = null,
    string? Replacement = null,
    string? ReplacementName = null,
    string? RuleId = null
);

/// <summary>An autocomplete suggestion for a partial marking string.</summary>
/// <param name="CategoryId">The category the concept belongs to.</param>
/// <param name="ConceptId">The id of the suggested concept.</param>
/// <param name="Description">Secondary label (longer name or alternate view).</param>
/// <param name="Group">Category label for UI grouping.</param>
/// <param name="Label">Primary display label in the current render format.</param>
/// <param name="Name">Human-readable concept name.</param>
/// <param name="Order">Sort order.</param>
/// <param name="Selected">True if this concept is already in the current marking.</param>
/// <param name="Views">All views for this concept.</param>
/// <param name="Gated">True when this concept matches the active filter but is currently gated by the scheme's rules (a rule prerequisite is unmet, or a constraint forbids it). Gated suggestions are surfaced as non-selectable, explanatory rows so the user can see why the concept is unavailable. Only populated when the caller passes <c>includeGated: true</c> and a filter is active.</param>
/// <param name="Requires">When <c>gated</c> is true, the unmet prerequisite concept ids that would unlock this concept. Empty array when the concept is gated by a constraint with no specific prerequisite (e.g. mutual exclusion).</param>
/// <param name="RequiresDisplay">Resolved operational display (rendered marking view) for each id in <c>requires</c>, index-aligned, so the unlock row can read "add X first" with the marking rather than the raw id. <c>requires</c> stays as the machine keys.</param>
/// <param name="Source"><c>preset</c> on a row that offers a label preset, whose <c>conceptId</c> is <c>preset:</c> followed by the preset id. A concept row leaves it unset.</param>
public record Suggestion(
    string CategoryId,
    string ConceptId,
    string Description,
    string Group,
    string Label,
    string Name,
    int Order,
    bool Selected,
    IReadOnlyDictionary<string, string> Views,
    bool? Gated = null,
    IReadOnlyList<string>? Requires = null,
    IReadOnlyList<string>? RequiresDisplay = null,
    string? Source = null
);

/// <summary>What suggest answers: the concepts a picker may offer, and nothing about the marking itself.</summary>
/// <param name="Suggestions">The concepts on offer, in display order.</param>
/// <param name="Warnings">Advisories raised while building the options, such as a concept source that could not answer in full. Empty when there were none. Advisories about the marking itself belong to validate and process.</param>
public record SuggestResult(
    IReadOnlyList<Suggestion> Suggestions,
    IReadOnlyList<Warning> Warnings
);

/// <summary>Portable scheme-default background and text colors for a resolved marking.</summary>
/// <param name="Background">Background color as a CSS hex string.</param>
/// <param name="Text">Text/foreground color as a CSS hex string.</param>
public record ConceptColor(
    string Background,
    string Text
);

/// <summary>One span of recognized input, by offset and source text.</summary>
/// <param name="End">End offset in the input string.</param>
/// <param name="Start">Start offset in the input string.</param>
/// <param name="Text">The text at that span.</param>
public record InputSpan(
    int End,
    int Start,
    string Text
);

/// <summary>One input span rewritten to its canonical representation.</summary>
/// <param name="End">End offset in the input string.</param>
/// <param name="Original">The text as supplied.</param>
/// <param name="Reason">What kind of departure from canonical this was.</param>
/// <param name="Replacement">The canonical text it was read as.</param>
/// <param name="Start">Start offset in the input string.</param>
public record RecognitionCorrection(
    int End,
    string Original,
    RecognitionCorrectionReason Reason,
    string Replacement,
    int Start
);

/// <summary>What recognition made of an input string: how far it got, what it was read as, and what it could not account for.</summary>
/// <param name="Status">How far recognition got.</param>
/// <param name="Unmatched">Input not accounted for as content, syntax, or permitted whitespace.</param>
/// <param name="InputFormat">What the text was read as, when that is decided.</param>
/// <param name="Role">Whether the text is spelled as a whole or a part marking: the role of the view it was read as, or, when no one view is decided, the role the scheme's <c>render.surfaces</c> decides for it.</param>
/// <param name="Surface">The surface the input was read as, by scheme scope id.</param>
public record RecognitionResult(
    RecognitionStatus Status,
    IReadOnlyList<InputSpan> Unmatched,
    string? InputFormat = null,
    StructuralRole? Role = null,
    string? Surface = null
);

/// <summary>What recognition made of an input string, with the corrections that turn it into canonical text. Only process returns this form.</summary>
/// <param name="Corrections">Departures from canonical that recognition read through.</param>
/// <param name="Status">How far recognition got.</param>
/// <param name="Unmatched">Input not accounted for as content, syntax, or permitted whitespace.</param>
/// <param name="InputFormat">What the text was read as, when that is decided.</param>
/// <param name="Role">Whether the text is spelled as a whole or a part marking: the role of the view it was read as, or, when no one view is decided, the role the scheme's <c>render.surfaces</c> decides for it.</param>
/// <param name="Surface">The surface the input was read as, by scheme scope id.</param>
public record RecognitionWithCorrections(
    IReadOnlyList<RecognitionCorrection> Corrections,
    RecognitionStatus Status,
    IReadOnlyList<InputSpan> Unmatched,
    string? InputFormat = null,
    StructuralRole? Role = null,
    string? Surface = null
);

/// <summary>What validate answers about one marking: the facts it states once settled, whether they stand, and what a reader needs to show why not.</summary>
/// <param name="AppliedFixes">Every change the engine actually made while settling the marking: a departure from canonical spelling or order that recognition read through, a repair, a fact a rule required, a handed-in fact settling removed, or text read in the other role from the one the request names. A removed fact is named with the reason it was removed, including a fact handed in with a derived source label that the run does not derive again. A requirement that conflicts with an existing concept is not added and appears in errors and proposed fixes, with no applied change. Repair never removes an author-stated concept. These are the reasons a marking can be <c>valid</c> while <c>inputWasValid</c> is false.</param>
/// <param name="Assertions">The facts the marking states, as the engine settled them.</param>
/// <param name="Assessment">The assessment of <c>rendered</c>: what it wrote of these facts, what it left out, what collapsed into an equivalent, and what it could not write at all. When <c>rendered</c> carries more than one surface, this is the combined assessment <c>RenderResult</c> describes. Read it before treating <c>rendered</c> as a transport.</param>
/// <param name="Errors">What the scheme's rules find wrong with the settled marking. An entry whose severity is <c>error</c> makes <c>valid</c> false.</param>
/// <param name="InputWasValid">True when the text or facts as given needed no change to stand.</param>
/// <param name="Notices">The notices the scheme attaches to the concepts in <c>assertions</c>, collected the same way the notices operation collects them. Always present, empty when the marking carries none, so a consumer can read the field without checking that it exists.</param>
/// <param name="Rendered">The settled marking, in one string.</param>
/// <param name="Valid">True when the marking stands once the engine has settled it: read, repaired, completed by what the scheme's rules imply, and judged with no error left.</param>
/// <param name="Warnings">Advisories that do not affect <c>valid</c>, such as a deprecated concept. On a process result whose fix paths were not all proven, this carries a notice whose <c>ruleId</c> is <c>_fixes_unproven</c>, as <c>ProcessResult.proposedFixes</c> describes.</param>
/// <param name="DisplayColor">Portable scheme-derived display color for the settled marking. Embedding hosts may override this presentation default.</param>
/// <param name="Recognition">What recognition made of the text: how far it got, what it read the text as, and what it could not account for.</param>
public record ValidateResult(
    IReadOnlyList<Change> AppliedFixes,
    IReadOnlyList<Assertion> Assertions,
    RenderAssessment Assessment,
    IReadOnlyList<ValidationError> Errors,
    bool InputWasValid,
    IReadOnlyList<Notice> Notices,
    string Rendered,
    bool Valid,
    IReadOnlyList<Warning> Warnings,
    ConceptColor? DisplayColor = null,
    RecognitionResult? Recognition = null
);

/// <summary>What process answers: everything validate answers, plus what an author needs to correct and finish the marking.</summary>
/// <param name="AppliedFixes">Every change the engine actually made while settling the marking: a departure from canonical spelling or order that recognition read through, a repair, a fact a rule required, a handed-in fact settling removed, or text read in the other role from the one the request names. A removed fact is named with the reason it was removed, including a fact handed in with a derived source label that the run does not derive again. A requirement that conflicts with an existing concept is not added and appears in errors and proposed fixes, with no applied change. Repair never removes an author-stated concept. These are the reasons a marking can be <c>valid</c> while <c>inputWasValid</c> is false.</param>
/// <param name="Assertions">The facts the marking states, as the engine settled them.</param>
/// <param name="Assessment">The assessment of <c>rendered</c>: what it wrote of these facts, what it left out, what collapsed into an equivalent, and what it could not write at all. When <c>rendered</c> carries more than one surface, this is the combined assessment <c>RenderResult</c> describes. Read it before treating <c>rendered</c> as a transport.</param>
/// <param name="Errors">What the scheme's rules find wrong with the settled marking. An entry whose severity is <c>error</c> makes <c>valid</c> false.</param>
/// <param name="InputWasValid">True when the text or facts as given needed no change to stand.</param>
/// <param name="Notices">The notices the scheme attaches to the concepts in <c>assertions</c>, collected the same way the notices operation collects them. Always present, empty when the marking carries none, so a consumer can read the field without checking that it exists.</param>
/// <param name="ProposedFixes">The ways to make the resulting value valid, computed from that value after applied changes, each a whole marking the engine has read back and found valid before offering it. Empty when the marking is valid, or when no change the engine could prove would make it so. An error's <c>suggestedFix</c> names one of these by its <c>message</c>.</param>
/// <param name="Rendered">The settled marking, in one string.</param>
/// <param name="Renders">The outputs the request named in <c>outputs</c>, one entry each, keyed by output address. Empty when the request named none.</param>
/// <param name="Valid">True when the marking stands once the engine has settled it: read, repaired, completed by what the scheme's rules imply, and judged with no error left.</param>
/// <param name="Warnings">Advisories that do not affect <c>valid</c>, such as a deprecated concept. On a process result whose fix paths were not all proven, this carries a notice whose <c>ruleId</c> is <c>_fixes_unproven</c>, as <c>ProcessResult.proposedFixes</c> describes.</param>
/// <param name="AssertionsBeforeRepair">What was read before repair: the assertions the input stated, before the repair pass took anything away or rewrote it. A retired concept appears under its replacement's name, as it does in <c>assertions</c> and in the fixes.</param>
/// <param name="DisplayColor">Portable scheme-derived display color for the settled marking. Embedding hosts may override this presentation default.</param>
/// <param name="Recognition">What recognition made of the text, with the corrections that turn it into canonical text. Present exactly when there was text to recognize.</param>
/// <param name="SuggestionsFor">The options for each category the request named in <c>suggestFor</c>, answered against these settled facts exactly as <c>suggestForCategories</c> answers them, in the request's output format and surface.</param>
public record ProcessResult(
    IReadOnlyList<Change> AppliedFixes,
    IReadOnlyList<Assertion> Assertions,
    RenderAssessment Assessment,
    IReadOnlyList<ValidationError> Errors,
    bool InputWasValid,
    IReadOnlyList<Notice> Notices,
    IReadOnlyList<Fix> ProposedFixes,
    string Rendered,
    IReadOnlyDictionary<string, RenderResult> Renders,
    bool Valid,
    IReadOnlyList<Warning> Warnings,
    IReadOnlyList<Assertion>? AssertionsBeforeRepair = null,
    ConceptColor? DisplayColor = null,
    RecognitionWithCorrections? Recognition = null,
    IReadOnlyDictionary<string, SuggestResult>? SuggestionsFor = null
);

/// <summary>Output of combining multiple inputs into a single rendered marking.</summary>
/// <param name="AppliedFixes">The changes the rollup made to reach the combined marking.</param>
/// <param name="Assertions">The assertions of the combined marking, listed in print order as a validate result's are: the order the rendered marking places each concept; a concept the rendering leaves out appears where it would print.</param>
/// <param name="Errors">The constraints the combined marking violates.</param>
/// <param name="Notices">The notices the scheme attaches to the concepts in <c>assertions</c>, collected the same way the notices operation collects them. Always present, empty when the aggregate carries none, so a consumer can read the field without checking that it exists.</param>
/// <param name="ProposedFixes">Always empty: aggregate applies every fix it can.</param>
/// <param name="Rendered">The combined marking, written in the output format.</param>
/// <param name="Valid">True when the combined marking stands once settled, as <c>ValidateResult.valid</c> defines it.</param>
/// <param name="Warnings">Advisory signals about the combined marking.</param>
/// <param name="DisplayColor">Portable scheme-derived display color for the final post-rollup marking. Embedding hosts may override this presentation default.</param>
public record AggregateResult(
    IReadOnlyList<Change> AppliedFixes,
    IReadOnlyList<Assertion> Assertions,
    IReadOnlyList<ValidationError> Errors,
    IReadOnlyList<Notice> Notices,
    IReadOnlyList<Fix> ProposedFixes,
    string Rendered,
    bool Valid,
    IReadOnlyList<Warning> Warnings,
    ConceptColor? DisplayColor = null
);

/// <summary>Comparison of two assertion sets: added, removed, and common assertions.</summary>
/// <param name="Added">Assertions in the second marking and not the first.</param>
/// <param name="Common">Assertions in both markings, as the first marking holds them. Concept and category decide sameness; confidence, source and value are not compared.</param>
/// <param name="Removed">Assertions in the first marking and not the second.</param>
public record DiffResult(
    IReadOnlyList<Assertion> Added,
    IReadOnlyList<Assertion> Common,
    IReadOnlyList<Assertion> Removed
);

/// <summary>A single marking found during a free-text scan of the source text, with what validate answers for it.</summary>
/// <param name="End">End position in the (possibly stripped) input text</param>
/// <param name="Marking">The matched marking string (as found in the document)</param>
/// <param name="Result">What validate answers for this marking, read where it was found and in the role <c>role</c> names. A scan reads documents; it does not author them, so it carries no fix paths. A caller that wants to correct one region processes that region.</param>
/// <param name="Role">Scheme-resolved structural role of this marking: <c>'whole'</c> (a document-wide marking: a page-top marking, a TLP document tag) or <c>'part'</c> (a per-region marking: a paragraph mark, a segment, a tag).</param>
/// <param name="Start">Start position in the (possibly stripped) input text</param>
/// <param name="Type">The output this marking is written in when the scan names no output format: <c>result.rendered</c> is written in it.</param>
/// <param name="Wrapped">Whether the marking was found inside a wrapper pattern (e.g., parentheses)</param>
/// <param name="Inner">The marking text inside the wrapper: the capture group of the scheme's <c>embeddedMarking</c> pattern. Present only when <c>wrapped</c> is true. Consumers that need to re-process the marking must use this rather than stripping delimiters from <c>marking</c>, because the wrapper shape is scheme-declared and is not always a pair of parentheses.</param>
public record ScanMatch(
    int End,
    string Marking,
    ValidateResult Result,
    StructuralRole Role,
    int Start,
    string Type,
    bool Wrapped,
    string? Inner = null
);

/// <summary>How far a scan got, and what stopped it when something did.</summary>
/// <param name="RegionsFound">Candidate regions the scan considered.</param>
/// <param name="Status">Whether the document was read to the end, and if not, why not.</param>
/// <param name="LimitHit">The ceiling that stopped the scan, when one did.</param>
/// <param name="RegionsCapped">Whether the per-document region cap was reached.</param>
public record ScanLimits(
    int RegionsFound,
    ScanStatus Status,
    RecognitionLimit? LimitHit = null,
    bool? RegionsCapped = null
);

/// <summary>A byte-offset span where a guide rule keyword or pattern matched in scanned text.</summary>
/// <param name="End">Offset in the scanned text just past the end of the match, in UTF-16 code units.</param>
/// <param name="Keyword">The keyword that matched, or the label of the matcher that did.</param>
/// <param name="Start">Offset in the scanned text where the match starts, in UTF-16 code units.</param>
/// <param name="Confidence">Advisory confidence in [0, 1], present only when the scan asked for it with the <c>confidence</c> scan option. An ordinal hint derived from the match's shape, not a calibrated probability.</param>
public record GuideScanSpan(
    int End,
    string Keyword,
    int Start,
    double? Confidence = null
);

/// <summary>A rule in a marking guide, paired with its scan metadata.</summary>
/// <param name="Id">Unique identifier within the guide.</param>
/// <param name="Name">Human-readable display name.</param>
/// <param name="Outcome">What this rule contributes when active. At least one of rendered or concepts must be set.</param>
/// <param name="Category">Guide category ID for grouping in the UI.</param>
/// <param name="Description">Description of the data element this rule covers.</param>
/// <param name="Keywords">Searchable terms for literal keyword filtering (absent on computed rules).</param>
/// <param name="Match">Named pattern matchers for content evaluation. Each entry is a <c>MatchNode</c>: a leaf spec referencing a built-in matcher (e.g. <c>{ type: 'ipv4' }</c>) or a custom regex (<c>{ type: 'regex', pattern: '...' }</c>), or a combinator node (<c>any</c> / <c>all</c> / <c>not</c> / <c>near</c> / <c>context</c> / <c>validate</c>) composed over child nodes. When content is evaluated, both <c>keywords</c> and <c>match</c> are checked.</param>
/// <param name="Overrides">Rule IDs whose outcomes this rule replaces when active. When omitted (default), this rule's outcome is additive: it contributes alongside the rules it depends on.</param>
/// <param name="Rationale">Explanation of why this outcome applies.</param>
/// <param name="When">When present, this rule auto-activates based on other rules' selection state. Uses the same when framework as scheme rules. <c>has</c> references guide rule IDs. Rules with <c>when</c> are "computed"; they don't appear as checkboxes.</param>
public record GuideRule(
    string Id,
    string Name,
    JsonElement Outcome,
    string? Category = null,
    string? Description = null,
    IReadOnlyList<string>? Keywords = null,
    IReadOnlyList<JsonElement>? Match = null,
    IReadOnlyList<string>? Overrides = null,
    string? Rationale = null,
    JsonElement? When = null
);

/// <summary>A guide rule matched by text scanning, with keyword spans.</summary>
/// <param name="MatchedKeywords">Each keyword or matcher label that fired, once each.</param>
/// <param name="Rule">The guide rule that matched.</param>
/// <param name="Spans">Every place in the text where the rule fired, in the order found.</param>
/// <param name="GuideId">The id of the guide the rule belongs to, when the scan covered more than one.</param>
/// <param name="Provenance">Source provenance for the guide-rule source that supplied the matched rule.</param>
public record GuideScanResult(
    IReadOnlyList<string> MatchedKeywords,
    GuideRule Rule,
    IReadOnlyList<GuideScanSpan> Spans,
    string? GuideId = null,
    SourceReference? Provenance = null
);

/// <summary>Output of evaluating selected guide rules into a single aggregate marking.</summary>
/// <param name="ActiveComputedRules">The computed rules whose <c>when</c> condition the selection satisfied.</param>
/// <param name="AggregatedMarking">The effective markings rolled up into one marking by the target scheme. Empty when no rule contributed, or when the result spans more than one target scheme, in which case <c>aggregatedMarkings</c> holds one per scheme.</param>
/// <param name="Breakdown">One entry per evaluated rule: for each guide, its selected rules, then its computed rules.</param>
/// <param name="ContributingBreakdown">Subset of <c>breakdown</c> that actually contributed to <c>aggregatedMarking</c> (i.e. <c>breakdown.filter(b =&gt; !b.wasOverridden)</c>). Precomputed so UI layers stop re-filtering the full breakdown.</param>
/// <param name="EffectiveMarkings">The marking each contributing rule's outcome resolves to. Overridden rules contribute none.</param>
/// <param name="SelectedRules">The selected rules that were evaluated. Rules with a <c>when</c> condition are computed, not selected, and are not listed here.</param>
/// <param name="AggregatedMarkings">Aggregated output keyed by target scheme id. Present when guide evaluation routed through more than one target scheme.</param>
/// <param name="GuideId">The id of the guide evaluated. Absent when the result combines several guides.</param>
/// <param name="Provenance">External guide-rule source observation used by this evaluation.</param>
public record EvaluationResult(
    IReadOnlyList<GuideRule> ActiveComputedRules,
    string AggregatedMarking,
    IReadOnlyList<GuideBreakdownEntry> Breakdown,
    IReadOnlyList<GuideBreakdownEntry> ContributingBreakdown,
    IReadOnlyList<string> EffectiveMarkings,
    IReadOnlyList<GuideRule> SelectedRules,
    IReadOnlyDictionary<string, string>? AggregatedMarkings = null,
    string? GuideId = null,
    SourceReference? Provenance = null
);

/// <summary>One deprecated-to-current concept substitution that processing applied before an access rule was evaluated.</summary>
/// <param name="ConceptDisplay">Resolved operational display for <c>conceptId</c>.</param>
/// <param name="ConceptId">Current concept the access rule was evaluated against.</param>
/// <param name="ReplacedConceptDisplay">Resolved operational display for <c>replacedConceptId</c>.</param>
/// <param name="ReplacedConceptId">Deprecated concept the data was actually marked with.</param>
public record ConceptModernization(
    string ConceptDisplay,
    string ConceptId,
    string ReplacedConceptDisplay,
    string ReplacedConceptId
);

/// <summary>Result of evaluating a single access rule entry.</summary>
/// <param name="Activation">Machine-readable activation basis.</param>
/// <param name="Comparisons">Machine-readable comparisons evaluated by the rule.</param>
/// <param name="Granted">Whether this rule passed.</param>
/// <param name="Name">Rule name from the access policy (for display).</param>
/// <param name="Rule">Qualified rule identity and semantic digest.</param>
/// <param name="RuleIndex">Zero-based index of the rule in the access policy's evaluated rules.</param>
/// <param name="Skipped">Whether the rule was skipped because its activation did not match.</param>
/// <param name="ModernizedFrom">Deprecated-to-current concept substitutions that processing applied to the data before this rule was evaluated.</param>
/// <param name="Reason">Denial reason from the rule definition or generated explanation.</param>
public record AccessRuleResult(
    AccessRuleActivation Activation,
    IReadOnlyList<AccessComparisonResult> Comparisons,
    bool Granted,
    string Name,
    RuleReference Rule,
    int RuleIndex,
    bool Skipped,
    IReadOnlyList<ConceptModernization>? ModernizedFrom = null,
    string? Reason = null
);

/// <summary>Overall access decision with per-rule breakdown for a labeled artifact.</summary>
/// <param name="Granted">True when every rule that was not skipped granted access.</param>
/// <param name="Rules">One result per rule in the policy, in policy order.</param>
/// <param name="Provenance">Source provenance for the access-rule source that evaluated this decision.</param>
public record AccessDecision(
    bool Granted,
    IReadOnlyList<AccessRuleResult> Rules,
    SourceReference? Provenance = null
);
