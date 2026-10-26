#nullable enable

// Wire enums. Every value is a kebab-case string token on the wire. The API
// contract is open-response and closed-request, so each enum a response carries
// is an extensible struct over its raw token (the Azure SDK pattern): a value a
// newer server adds decodes, keeps its exact token, and goes back unchanged when
// a caller sends it in the next request. The server, not this client, decides
// whether a request may carry a token. Parse and TryParse are strict for tokens
// a caller writes by hand.
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EzClassify;

/// <summary>How the engine arrived at an assertion during parsing.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<Confidence>))]
public readonly struct Confidence : IEquatable<Confidence>, IWireToken<Confidence>
{
    private const string CertainValue = "certain";
    private const string InferredValue = "inferred";
    private const string RepairedValue = "repaired";
    private const string UncertainValue = "uncertain";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public Confidence(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>An exact token match.</summary>
    public static Confidence Certain { get; } = new(CertainValue);

    /// <summary>Derived by inference rules rather than parsed directly.</summary>
    public static Confidence Inferred { get; } = new(InferredValue);

    /// <summary>Produced by a repair of malformed input.</summary>
    public static Confidence Repaired { get; } = new(RepairedValue);

    /// <summary>A low-confidence guess the caller should review.</summary>
    public static Confidence Uncertain { get; } = new(UncertainValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is CertainValue or InferredValue or RepairedValue or UncertainValue;

    /// <summary>Decode a token a caller supplies; only a declared confidence value is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a confidence value this SDK declares.</exception>
    public static Confidence Parse(string value) => WireToken.Parse<Confidence>(value, "confidence value");

    /// <summary>Decode a token a caller supplies without throwing; only a declared confidence value is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out Confidence result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(Confidence)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(Confidence));

    /// <summary>The wire token; empty for <c>default(Confidence)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(Confidence other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Confidence other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(Confidence left, Confidence right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(Confidence left, Confidence right) => !left.Equals(right);

    string? IWireToken<Confidence>.Token => _value;

    Confidence IWireToken<Confidence>.FromToken(string value) => new(value);
}

/// <summary>Whether a validation finding blocks the marking or merely flags it.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<Severity>))]
public readonly struct Severity : IEquatable<Severity>, IWireToken<Severity>
{
    private const string ErrorValue = "error";
    private const string WarningValue = "warning";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public Severity(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>A blocking problem: the marking is invalid and must be corrected.</summary>
    public static Severity Error { get; } = new(ErrorValue);

    /// <summary>A non-blocking concern the caller may surface but need not act on.</summary>
    public static Severity Warning { get; } = new(WarningValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is ErrorValue or WarningValue;

    /// <summary>Decode a token a caller supplies; only a declared severity value is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a severity value this SDK declares.</exception>
    public static Severity Parse(string value) => WireToken.Parse<Severity>(value, "severity value");

    /// <summary>Decode a token a caller supplies without throwing; only a declared severity value is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out Severity result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(Severity)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(Severity));

    /// <summary>The wire token; empty for <c>default(Severity)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(Severity other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Severity other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(Severity left, Severity right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(Severity left, Severity right) => !left.Equals(right);

    string? IWireToken<Severity>.Token => _value;

    Severity IWireToken<Severity>.FromToken(string value) => new(value);
}

/// <summary>What class of advisory a <see cref="Warning"/> carries.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<WarningKind>))]
public readonly struct WarningKind : IEquatable<WarningKind>, IWireToken<WarningKind>
{
    private const string DeprecationValue = "deprecation";
    private const string RuleValue = "rule";
    private const string NoticeValue = "notice";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public WarningKind(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>A deprecated concept is present in the marking.</summary>
    public static WarningKind Deprecation { get; } = new(DeprecationValue);

    /// <summary>A scheme's authored <c>warn</c> rule matched the marking.</summary>
    public static WarningKind Rule { get; } = new(RuleValue);

    /// <summary>The engine's own notice about the operation, such as a part not compared with its whole.</summary>
    public static WarningKind Notice { get; } = new(NoticeValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is DeprecationValue or RuleValue or NoticeValue;

    /// <summary>Decode a token a caller supplies; only a declared warning kind is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a warning kind this SDK declares.</exception>
    public static WarningKind Parse(string value) => WireToken.Parse<WarningKind>(value, "warning kind");

    /// <summary>Decode a token a caller supplies without throwing; only a declared warning kind is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out WarningKind result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(WarningKind)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(WarningKind));

    /// <summary>The wire token; empty for <c>default(WarningKind)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(WarningKind other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is WarningKind other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(WarningKind left, WarningKind right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(WarningKind left, WarningKind right) => !left.Equals(right);

    string? IWireToken<WarningKind>.Token => _value;

    WarningKind IWireToken<WarningKind>.FromToken(string value) => new(value);
}

/// <summary>The structural role of a scanned marking: document-wide or per-region.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<StructuralRole>))]
public readonly struct StructuralRole : IEquatable<StructuralRole>, IWireToken<StructuralRole>
{
    private const string WholeValue = "whole";
    private const string PartValue = "part";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public StructuralRole(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>A document-wide marking (page-top banner, TLP document tag).</summary>
    public static StructuralRole Whole { get; } = new(WholeValue);

    /// <summary>A per-region marking (a portion mark on a paragraph, a segment tag).</summary>
    public static StructuralRole Part { get; } = new(PartValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is WholeValue or PartValue;

    /// <summary>Decode a token a caller supplies; only a declared structural role is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a structural role this SDK declares.</exception>
    public static StructuralRole Parse(string value) => WireToken.Parse<StructuralRole>(value, "structural role");

    /// <summary>Decode a token a caller supplies without throwing; only a declared structural role is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out StructuralRole result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(StructuralRole)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(StructuralRole));

    /// <summary>The wire token; empty for <c>default(StructuralRole)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(StructuralRole other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is StructuralRole other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(StructuralRole left, StructuralRole right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(StructuralRole left, StructuralRole right) => !left.Equals(right);

    string? IWireToken<StructuralRole>.Token => _value;

    StructuralRole IWireToken<StructuralRole>.FromToken(string value) => new(value);
}

/// <summary>What one scheme is to the Context that holds it. A scheme can be more than one at once: a crosswalk's source is often the Context's own primary scheme.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<ContextSchemeRole>))]
public readonly struct ContextSchemeRole : IEquatable<ContextSchemeRole>, IWireToken<ContextSchemeRole>
{
    private const string PrimaryValue = "primary";
    private const string CrosswalkSourceValue = "crosswalk-source";
    private const string CrosswalkTargetValue = "crosswalk-target";
    private const string AdditionalValue = "additional";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public ContextSchemeRole(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The Context's own marking scheme, the base of its composition.</summary>
    public static ContextSchemeRole Primary { get; } = new(PrimaryValue);

    /// <summary>The scheme a crosswalk translates from.</summary>
    public static ContextSchemeRole CrosswalkSource { get; } = new(CrosswalkSourceValue);

    /// <summary>The scheme a crosswalk translates into.</summary>
    public static ContextSchemeRole CrosswalkTarget { get; } = new(CrosswalkTargetValue);

    /// <summary>A scheme the composition loaded without naming it as either; the Context holds it and nothing more is said.</summary>
    public static ContextSchemeRole Additional { get; } = new(AdditionalValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is PrimaryValue or CrosswalkSourceValue or CrosswalkTargetValue or AdditionalValue;

    /// <summary>Decode a token a caller supplies; only a declared context scheme role is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a context scheme role this SDK declares.</exception>
    public static ContextSchemeRole Parse(string value) => WireToken.Parse<ContextSchemeRole>(value, "context scheme role");

    /// <summary>Decode a token a caller supplies without throwing; only a declared context scheme role is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out ContextSchemeRole result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(ContextSchemeRole)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(ContextSchemeRole));

    /// <summary>The wire token; empty for <c>default(ContextSchemeRole)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(ContextSchemeRole other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ContextSchemeRole other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(ContextSchemeRole left, ContextSchemeRole right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(ContextSchemeRole left, ContextSchemeRole right) => !left.Equals(right);

    string? IWireToken<ContextSchemeRole>.Token => _value;

    ContextSchemeRole IWireToken<ContextSchemeRole>.FromToken(string value) => new(value);
}

/// <summary>The kind of change a <see cref="Fix"/> records: what the engine did to a marking, or could do.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<FixAction>))]
public readonly struct FixAction : IEquatable<FixAction>, IWireToken<FixAction>
{
    private const string AddValue = "add";
    private const string RemoveValue = "remove";
    private const string ReplaceValue = "replace";
    private const string ReorderValue = "reorder";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public FixAction(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The engine added a concept (an inference rule fired, etc.).</summary>
    public static FixAction Add { get; } = new(AddValue);

    /// <summary>The engine removed a concept (a superseded marking, etc.).</summary>
    public static FixAction Remove { get; } = new(RemoveValue);

    /// <summary>The engine replaced one concept with another.</summary>
    public static FixAction Replace { get; } = new(ReplaceValue);

    /// <summary>
    /// Input parsed via a tolerant rule (non-canonical token order). No concept
    /// was added or removed; the canonical form is in the result's rendered marking.
    /// </summary>
    public static FixAction Reorder { get; } = new(ReorderValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is AddValue or RemoveValue or ReplaceValue or ReorderValue;

    /// <summary>Decode a token a caller supplies; only a declared fix action is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a fix action this SDK declares.</exception>
    public static FixAction Parse(string value) => WireToken.Parse<FixAction>(value, "fix action");

    /// <summary>Decode a token a caller supplies without throwing; only a declared fix action is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out FixAction result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(FixAction)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(FixAction));

    /// <summary>The wire token; empty for <c>default(FixAction)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(FixAction other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FixAction other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(FixAction left, FixAction right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(FixAction left, FixAction right) => !left.Equals(right);

    string? IWireToken<FixAction>.Token => _value;

    FixAction IWireToken<FixAction>.FromToken(string value) => new(value);
}

/// <summary>The overall verdict of a document audit.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<AuditVerdict>))]
public readonly struct AuditVerdict : IEquatable<AuditVerdict>, IWireToken<AuditVerdict>
{
    private const string PassValue = "pass";
    private const string MixedValue = "mixed";
    private const string InadequateCoverageValue = "inadequate-coverage";
    private const string ProceduralGapValue = "procedural-gap";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public AuditVerdict(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>Every part is adequately covered by the whole marking.</summary>
    public static AuditVerdict Pass { get; } = new(PassValue);

    /// <summary>Some parts pass and some fail.</summary>
    public static AuditVerdict Mixed { get; } = new(MixedValue);

    /// <summary>The whole marking does not cover every part.</summary>
    public static AuditVerdict InadequateCoverage { get; } = new(InadequateCoverageValue);

    /// <summary>A procedural requirement (e.g. a missing whole marking) failed.</summary>
    public static AuditVerdict ProceduralGap { get; } = new(ProceduralGapValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is PassValue or MixedValue or InadequateCoverageValue or ProceduralGapValue;

    /// <summary>Decode a token a caller supplies; only a declared audit verdict is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a audit verdict this SDK declares.</exception>
    public static AuditVerdict Parse(string value) => WireToken.Parse<AuditVerdict>(value, "audit verdict");

    /// <summary>Decode a token a caller supplies without throwing; only a declared audit verdict is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out AuditVerdict result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(AuditVerdict)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(AuditVerdict));

    /// <summary>The wire token; empty for <c>default(AuditVerdict)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(AuditVerdict other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is AuditVerdict other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(AuditVerdict left, AuditVerdict right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(AuditVerdict left, AuditVerdict right) => !left.Equals(right);

    string? IWireToken<AuditVerdict>.Token => _value;

    AuditVerdict IWireToken<AuditVerdict>.FromToken(string value) => new(value);
}

/// <summary>Whether rendering may project facts or must preserve every fact.</summary>
public enum RenderPreservation
{
    /// <summary>Return a useful projection and report any loss in the assessment.</summary>
    Projection,

    /// <summary>Refuse the render if any fact would be lost.</summary>
    Full,
}

/// <summary>Why a render format cannot write a fact it was given.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<UnrepresentableFactReason>))]
public readonly struct UnrepresentableFactReason : IEquatable<UnrepresentableFactReason>, IWireToken<UnrepresentableFactReason>
{
    private const string UnframableValue = "unframable";
    private const string ReservedLiteralValue = "reserved-literal";
    private const string AmbiguousIdentityValue = "ambiguous-identity";
    private const string NoFieldValue = "no-field";
    private const string NoViewValue = "no-view";
    private const string ValueLossyValue = "value-lossy";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public UnrepresentableFactReason(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The value cannot be framed without changing its text.</summary>
    public static UnrepresentableFactReason Unframable { get; } = new(UnframableValue);

    /// <summary>The value is reserved by a fixed literal in the same format.</summary>
    public static UnrepresentableFactReason ReservedLiteral { get; } = new(ReservedLiteralValue);

    /// <summary>The value would read back as a different typed fact.</summary>
    public static UnrepresentableFactReason AmbiguousIdentity { get; } = new(AmbiguousIdentityValue);

    /// <summary>The format has no field for the fact.</summary>
    public static UnrepresentableFactReason NoField { get; } = new(NoFieldValue);

    /// <summary>The format's view has no spelling for the concept.</summary>
    public static UnrepresentableFactReason NoView { get; } = new(NoViewValue);

    /// <summary>The format would discard the assertion's captured value.</summary>
    public static UnrepresentableFactReason ValueLossy { get; } = new(ValueLossyValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is UnframableValue or ReservedLiteralValue or AmbiguousIdentityValue or NoFieldValue or NoViewValue or ValueLossyValue;

    /// <summary>Decode a token a caller supplies; only a declared unrepresentable-fact reason is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not an unrepresentable-fact reason this SDK declares.</exception>
    public static UnrepresentableFactReason Parse(string value) => WireToken.Parse<UnrepresentableFactReason>(value, "unrepresentable-fact reason");

    /// <summary>Decode a token a caller supplies without throwing; only a declared unrepresentable-fact reason is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out UnrepresentableFactReason result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(UnrepresentableFactReason)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(UnrepresentableFactReason));

    /// <summary>The wire token; empty for <c>default(UnrepresentableFactReason)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(UnrepresentableFactReason other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is UnrepresentableFactReason other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(UnrepresentableFactReason left, UnrepresentableFactReason right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(UnrepresentableFactReason left, UnrepresentableFactReason right) => !left.Equals(right);

    string? IWireToken<UnrepresentableFactReason>.Token => _value;

    UnrepresentableFactReason IWireToken<UnrepresentableFactReason>.FromToken(string value) => new(value);
}

/// <summary>Why an output was refused whole, before any of its facts was written.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<UnrepresentableOutputReason>))]
public readonly struct UnrepresentableOutputReason : IEquatable<UnrepresentableOutputReason>, IWireToken<UnrepresentableOutputReason>
{
    private const string UnresolvedContextValue = "unresolved-context";
    private const string AmbiguousRepresentationValue = "ambiguous-representation";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public UnrepresentableOutputReason(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The output's choice of format turns on a condition the operation cannot decide, such as one that reads context the caller did not supply.</summary>
    public static UnrepresentableOutputReason UnresolvedContext { get; } = new(UnresolvedContextValue);

    /// <summary>More than one of the output's representations is in force and nothing chooses between them, such as two of its cases holding at once.</summary>
    public static UnrepresentableOutputReason AmbiguousRepresentation { get; } = new(AmbiguousRepresentationValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is UnresolvedContextValue or AmbiguousRepresentationValue;

    /// <summary>Decode a token a caller supplies; only a declared unrepresentable-output reason is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not an unrepresentable-output reason this SDK declares.</exception>
    public static UnrepresentableOutputReason Parse(string value) => WireToken.Parse<UnrepresentableOutputReason>(value, "unrepresentable-output reason");

    /// <summary>Decode a token a caller supplies without throwing; only a declared unrepresentable-output reason is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out UnrepresentableOutputReason result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(UnrepresentableOutputReason)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(UnrepresentableOutputReason));

    /// <summary>The wire token; empty for <c>default(UnrepresentableOutputReason)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(UnrepresentableOutputReason other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is UnrepresentableOutputReason other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(UnrepresentableOutputReason left, UnrepresentableOutputReason right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(UnrepresentableOutputReason left, UnrepresentableOutputReason right) => !left.Equals(right);

    string? IWireToken<UnrepresentableOutputReason>.Token => _value;

    UnrepresentableOutputReason IWireToken<UnrepresentableOutputReason>.FromToken(string value) => new(value);
}

/// <summary>How far a scan got through the document it was given.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<ScanStatus>))]
public readonly struct ScanStatus : IEquatable<ScanStatus>, IWireToken<ScanStatus>
{
    private const string CompleteValue = "complete";
    private const string TruncatedValue = "truncated";
    private const string ResourceLimitValue = "resource-limit";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public ScanStatus(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The whole document was offered to recognition.</summary>
    public static ScanStatus Complete { get; } = new(CompleteValue);

    /// <summary>The per-document region cap was reached with budget to spare.</summary>
    public static ScanStatus Truncated { get; } = new(TruncatedValue);

    /// <summary>The work budget was spent, or the caller aborted.</summary>
    public static ScanStatus ResourceLimit { get; } = new(ResourceLimitValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is CompleteValue or TruncatedValue or ResourceLimitValue;

    /// <summary>Decode a token a caller supplies; only a declared scan status is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a scan status this SDK declares.</exception>
    public static ScanStatus Parse(string value) => WireToken.Parse<ScanStatus>(value, "scan status");

    /// <summary>Decode a token a caller supplies without throwing; only a declared scan status is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out ScanStatus result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(ScanStatus)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(ScanStatus));

    /// <summary>The wire token; empty for <c>default(ScanStatus)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(ScanStatus other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ScanStatus other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(ScanStatus left, ScanStatus right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(ScanStatus left, ScanStatus right) => !left.Equals(right);

    string? IWireToken<ScanStatus>.Token => _value;

    ScanStatus IWireToken<ScanStatus>.FromToken(string value) => new(value);
}

/// <summary>How far recognition got.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<RecognitionStatus>))]
public readonly struct RecognitionStatus : IEquatable<RecognitionStatus>, IWireToken<RecognitionStatus>
{
    private const string CompleteValue = "complete";
    private const string PartialValue = "partial";
    private const string AmbiguousValue = "ambiguous";
    private const string UnrecognizedValue = "unrecognized";
    private const string ResourceLimitValue = "resource-limit";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public RecognitionStatus(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>All input was accounted for by one reading.</summary>
    public static RecognitionStatus Complete { get; } = new(CompleteValue);

    /// <summary>Only part of the input was recognized.</summary>
    public static RecognitionStatus Partial { get; } = new(PartialValue);

    /// <summary>The input supports more than one distinct reading.</summary>
    public static RecognitionStatus Ambiguous { get; } = new(AmbiguousValue);

    /// <summary>No reading was recognized.</summary>
    public static RecognitionStatus Unrecognized { get; } = new(UnrecognizedValue);

    /// <summary>Recognition stopped at a configured work ceiling.</summary>
    public static RecognitionStatus ResourceLimit { get; } = new(ResourceLimitValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is CompleteValue or PartialValue or AmbiguousValue or UnrecognizedValue or ResourceLimitValue;

    /// <summary>Decode a token a caller supplies; only a declared recognition status is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a recognition status this SDK declares.</exception>
    public static RecognitionStatus Parse(string value) => WireToken.Parse<RecognitionStatus>(value, "recognition status");

    /// <summary>Decode a token a caller supplies without throwing; only a declared recognition status is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out RecognitionStatus result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(RecognitionStatus)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(RecognitionStatus));

    /// <summary>The wire token; empty for <c>default(RecognitionStatus)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(RecognitionStatus other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RecognitionStatus other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(RecognitionStatus left, RecognitionStatus right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(RecognitionStatus left, RecognitionStatus right) => !left.Equals(right);

    string? IWireToken<RecognitionStatus>.Token => _value;

    RecognitionStatus IWireToken<RecognitionStatus>.FromToken(string value) => new(value);
}

/// <summary>Why recognition rewrote one span of the input.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<RecognitionCorrectionReason>))]
public readonly struct RecognitionCorrectionReason : IEquatable<RecognitionCorrectionReason>, IWireToken<RecognitionCorrectionReason>
{
    private const string CaseValue = "case";
    private const string AliasValue = "alias";
    private const string OrderValue = "order";
    private const string SeparatorValue = "separator";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public RecognitionCorrectionReason(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>Letter case was canonicalized.</summary>
    public static RecognitionCorrectionReason Case { get; } = new(CaseValue);

    /// <summary>An alias was replaced with its canonical spelling.</summary>
    public static RecognitionCorrectionReason Alias { get; } = new(AliasValue);

    /// <summary>Token order was canonicalized.</summary>
    public static RecognitionCorrectionReason Order { get; } = new(OrderValue);

    /// <summary>A separator was canonicalized.</summary>
    public static RecognitionCorrectionReason Separator { get; } = new(SeparatorValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is CaseValue or AliasValue or OrderValue or SeparatorValue;

    /// <summary>Decode a token a caller supplies; only a declared recognition correction reason is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a recognition correction reason this SDK declares.</exception>
    public static RecognitionCorrectionReason Parse(string value) => WireToken.Parse<RecognitionCorrectionReason>(value, "recognition correction reason");

    /// <summary>Decode a token a caller supplies without throwing; only a declared recognition correction reason is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out RecognitionCorrectionReason result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(RecognitionCorrectionReason)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(RecognitionCorrectionReason));

    /// <summary>The wire token; empty for <c>default(RecognitionCorrectionReason)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(RecognitionCorrectionReason other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RecognitionCorrectionReason other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(RecognitionCorrectionReason left, RecognitionCorrectionReason right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(RecognitionCorrectionReason left, RecognitionCorrectionReason right) => !left.Equals(right);

    string? IWireToken<RecognitionCorrectionReason>.Token => _value;

    RecognitionCorrectionReason IWireToken<RecognitionCorrectionReason>.FromToken(string value) => new(value);
}

/// <summary>The recognition work ceiling that was reached.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<RecognitionLimit>))]
public readonly struct RecognitionLimit : IEquatable<RecognitionLimit>, IWireToken<RecognitionLimit>
{
    private const string AbortedValue = "aborted";
    private const string InputLengthValue = "input-length";
    private const string TransitionsValue = "transitions";
    private const string LookupsValue = "lookups";
    private const string LiveStatesValue = "live-states";
    private const string WallClockValue = "wall-clock";
    private const string GrammarSizeValue = "grammar-size";
    private const string DepthValue = "depth";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public RecognitionLimit(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The caller aborted recognition.</summary>
    public static RecognitionLimit Aborted { get; } = new(AbortedValue);

    /// <summary>The input length ceiling was reached.</summary>
    public static RecognitionLimit InputLength { get; } = new(InputLengthValue);

    /// <summary>The transition count ceiling was reached.</summary>
    public static RecognitionLimit Transitions { get; } = new(TransitionsValue);

    /// <summary>The vocabulary lookup ceiling was reached.</summary>
    public static RecognitionLimit Lookups { get; } = new(LookupsValue);

    /// <summary>The live-state ceiling was reached.</summary>
    public static RecognitionLimit LiveStates { get; } = new(LiveStatesValue);

    /// <summary>The elapsed-time ceiling was reached.</summary>
    public static RecognitionLimit WallClock { get; } = new(WallClockValue);

    /// <summary>The grammar-size ceiling was reached.</summary>
    public static RecognitionLimit GrammarSize { get; } = new(GrammarSizeValue);

    /// <summary>The recursion-depth ceiling was reached.</summary>
    public static RecognitionLimit Depth { get; } = new(DepthValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is AbortedValue or InputLengthValue or TransitionsValue or LookupsValue or LiveStatesValue or WallClockValue or GrammarSizeValue or DepthValue;

    /// <summary>Decode a token a caller supplies; only a declared recognition limit is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a recognition limit this SDK declares.</exception>
    public static RecognitionLimit Parse(string value) => WireToken.Parse<RecognitionLimit>(value, "recognition limit");

    /// <summary>Decode a token a caller supplies without throwing; only a declared recognition limit is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out RecognitionLimit result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(RecognitionLimit)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(RecognitionLimit));

    /// <summary>The wire token; empty for <c>default(RecognitionLimit)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(RecognitionLimit other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RecognitionLimit other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(RecognitionLimit left, RecognitionLimit right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(RecognitionLimit left, RecognitionLimit right) => !left.Equals(right);

    string? IWireToken<RecognitionLimit>.Token => _value;

    RecognitionLimit IWireToken<RecognitionLimit>.FromToken(string value) => new(value);
}

/// <summary>How a render format lays out its fields.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<RenderFormatMode>))]
public readonly struct RenderFormatMode : IEquatable<RenderFormatMode>, IWireToken<RenderFormatMode>
{
    private const string InlineValue = "inline";
    private const string BlockValue = "block";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public RenderFormatMode(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The fields run together on one line.</summary>
    public static RenderFormatMode Inline { get; } = new(InlineValue);

    /// <summary>Each field goes on its own line.</summary>
    public static RenderFormatMode Block { get; } = new(BlockValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is InlineValue or BlockValue;

    /// <summary>Decode a token a caller supplies; only a declared render format mode is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a render format mode this SDK declares.</exception>
    public static RenderFormatMode Parse(string value) => WireToken.Parse<RenderFormatMode>(value, "render format mode");

    /// <summary>Decode a token a caller supplies without throwing; only a declared render format mode is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out RenderFormatMode result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(RenderFormatMode)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(RenderFormatMode));

    /// <summary>The wire token; empty for <c>default(RenderFormatMode)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(RenderFormatMode other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RenderFormatMode other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(RenderFormatMode left, RenderFormatMode right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(RenderFormatMode left, RenderFormatMode right) => !left.Equals(right);

    string? IWireToken<RenderFormatMode>.Token => _value;

    RenderFormatMode IWireToken<RenderFormatMode>.FromToken(string value) => new(value);
}

/// <summary>Where a format writes a heading relative to the list it heads.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<FormatHeaderPosition>))]
public readonly struct FormatHeaderPosition : IEquatable<FormatHeaderPosition>, IWireToken<FormatHeaderPosition>
{
    private const string PrefixValue = "prefix";
    private const string SuffixValue = "suffix";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public FormatHeaderPosition(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>Before the list.</summary>
    public static FormatHeaderPosition Prefix { get; } = new(PrefixValue);

    /// <summary>After the list.</summary>
    public static FormatHeaderPosition Suffix { get; } = new(SuffixValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is PrefixValue or SuffixValue;

    /// <summary>Decode a token a caller supplies; only a declared format header position is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a format header position this SDK declares.</exception>
    public static FormatHeaderPosition Parse(string value) => WireToken.Parse<FormatHeaderPosition>(value, "format header position");

    /// <summary>Decode a token a caller supplies without throwing; only a declared format header position is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out FormatHeaderPosition result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(FormatHeaderPosition)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(FormatHeaderPosition));

    /// <summary>The wire token; empty for <c>default(FormatHeaderPosition)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(FormatHeaderPosition other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FormatHeaderPosition other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(FormatHeaderPosition left, FormatHeaderPosition right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(FormatHeaderPosition left, FormatHeaderPosition right) => !left.Equals(right);

    string? IWireToken<FormatHeaderPosition>.Token => _value;

    FormatHeaderPosition IWireToken<FormatHeaderPosition>.FromToken(string value) => new(value);
}

/// <summary>A render format's encoding.</summary>
[JsonConverter(typeof(WireTokenJsonConverter<FormatSyntaxType>))]
public readonly struct FormatSyntaxType : IEquatable<FormatSyntaxType>, IWireToken<FormatSyntaxType>
{
    private const string TextValue = "text";
    private const string XmlAttributesValue = "xml-attributes";

    private readonly string? _value;

    /// <summary>Wraps a wire token, whether or not this SDK declares it.</summary>
    /// <param name="value">The token exactly as it travels on the wire.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public FormatSyntaxType(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>Plain marking text.</summary>
    public static FormatSyntaxType Text { get; } = new(TextValue);

    /// <summary>XML attributes.</summary>
    public static FormatSyntaxType XmlAttributes { get; } = new(XmlAttributesValue);

    /// <summary>Whether this version of the SDK declares the token. False for a value a newer server sent.</summary>
    public bool IsKnown => _value is TextValue or XmlAttributesValue;

    /// <summary>Decode a token a caller supplies; only a declared format syntax type is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <returns>The declared value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a format syntax type this SDK declares.</exception>
    public static FormatSyntaxType Parse(string value) => WireToken.Parse<FormatSyntaxType>(value, "format syntax type");

    /// <summary>Decode a token a caller supplies without throwing; only a declared format syntax type is accepted.</summary>
    /// <param name="value">The wire token.</param>
    /// <param name="result">The declared value, or <c>default</c> when the token is not declared.</param>
    /// <returns>True when <paramref name="value"/> is a declared token.</returns>
    public static bool TryParse(string? value, out FormatSyntaxType result) => WireToken.TryParse(value, out result);

    /// <summary>The wire token, exactly as it was received or constructed.</summary>
    /// <exception cref="InvalidOperationException">The value is <c>default(FormatSyntaxType)</c>, which carries no token.</exception>
    public string ToWire() => WireToken.Require(_value, nameof(FormatSyntaxType));

    /// <summary>The wire token; empty for <c>default(FormatSyntaxType)</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public bool Equals(FormatSyntaxType other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FormatSyntaxType other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => WireToken.Hash(_value);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator ==(FormatSyntaxType left, FormatSyntaxType right) => left.Equals(right);

    /// <summary>Ordinal comparison of the wire tokens.</summary>
    public static bool operator !=(FormatSyntaxType left, FormatSyntaxType right) => !left.Equals(right);

    string? IWireToken<FormatSyntaxType>.Token => _value;

    FormatSyntaxType IWireToken<FormatSyntaxType>.FromToken(string value) => new(value);
}

/// <summary>
/// Wire codec for <see cref="RenderPreservation"/>, the one wire enum that only
/// travels in requests. Every other wire enum is an extensible struct that
/// carries its own token.
/// </summary>
public static class WireEnums
{
    /// <summary>Encode a render preservation value; throws for a value with no wire token.</summary>
    public static string ToWire(this RenderPreservation value) => value switch
    {
        RenderPreservation.Projection => "projection",
        RenderPreservation.Full => "full",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "RenderPreservation value has no wire token"),
    };
}

/// <summary>
/// What the shared codec needs from an extensible wire enum. Internal, and
/// implemented explicitly, so it stays off the public surface.
/// </summary>
/// <typeparam name="T">The implementing struct.</typeparam>
internal interface IWireToken<T> where T : struct, IWireToken<T>
{
    /// <summary>Whether this SDK declares the token.</summary>
    bool IsKnown { get; }

    /// <summary>The raw token; null only for <c>default(T)</c>.</summary>
    string? Token { get; }

    /// <summary>
    /// Wrap a token. Called on <c>default(T)</c>: netstandard2.0 has no static
    /// abstract members, so an instance method stands in for a factory.
    /// </summary>
    T FromToken(string value);
}

/// <summary>Shared behavior of the extensible wire enums.</summary>
internal static class WireToken
{
    /// <summary>Wrap a token read from an optional response field; null stays null.</summary>
    public static T? ReadOptional<T>(string? value) where T : struct, IWireToken<T> =>
        value is null ? null : default(T).FromToken(value);

    public static T Parse<T>(string value, string what) where T : struct, IWireToken<T>
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        return TryParse(value, out T result)
            ? result
            : throw new ArgumentException($"Unknown {what}: {value}", nameof(value));
    }

    public static bool TryParse<T>(string? value, out T result) where T : struct, IWireToken<T>
    {
        if (value is not null)
        {
            var candidate = default(T).FromToken(value);
            if (candidate.IsKnown)
            {
                result = candidate;
                return true;
            }
        }
        result = default;
        return false;
    }

    public static string Require(string? token, string typeName) =>
        token ?? throw new InvalidOperationException(
            $"default({typeName}) carries no wire token. Use a declared value, Parse, or the constructor.");

    // netstandard2.0 has no string.GetHashCode(StringComparison); the ordinal
    // comparer gives the same hash contract Equals relies on.
    public static int Hash(string? token) => token is null ? 0 : StringComparer.Ordinal.GetHashCode(token);
}

/// <summary>
/// System.Text.Json converter for the extensible wire enums: reads and writes the
/// raw token byte for byte, known or not.
/// </summary>
/// <typeparam name="T">The extensible enum struct.</typeparam>
internal sealed class WireTokenJsonConverter<T> : JsonConverter<T> where T : struct, IWireToken<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"{typeof(T).Name} must be a JSON string, not {reader.TokenType}.");
        return default(T).FromToken(reader.GetString() ?? throw new JsonException($"{typeof(T).Name} token is missing."));
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Token ?? throw new JsonException($"default({typeof(T).Name}) carries no wire token."));
}
