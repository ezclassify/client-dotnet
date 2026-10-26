using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace EzClassify.Tests;

/// <summary>
/// Holds every public record in the contract assembly to the shape the vendored
/// OpenAPI document declares for it.
/// </summary>
/// <remarks>
/// Many of these records are hand-written in the gen.mjs templates and read by
/// hand parsers, so regenerating from a new spec cannot add a field to them and
/// no other gate notices when the contract grows. This test fails when a
/// record lacks a property its contract shape declares, carries one the
/// contract does not, or disagrees with the contract about whether a scalar
/// may be absent: a non-nullable property for an optional field would make the
/// parser throw on a response that legitimately omits it. A deliberate
/// difference goes in one of the allowlists below with the reason for it, and
/// an entry that no longer applies fails too, as does a ContractSource entry
/// that names no record, so the lists cannot outlive the
/// gap they describe.
/// </remarks>
public class ContractFidelityTests
{
    /// <summary>
    /// Records whose contract shape is not the same-named schema component. The
    /// value is a path under <c>components/schemas</c>: a component name,
    /// optionally followed by the property path to an inline object.
    /// </summary>
    private static readonly Dictionary<string, string> ContractSource = new()
    {
        ["CoverageReport"] = "CheckCoverageResult",
        ["ScanMatch"] = "ScanResult",
        ["SchemeView"] = "SchemeViewResponse",
        ["SchemeViewCategory"] = "SchemeViewResponse/properties/categories/items",
        ["GuideView"] = "GuideViewResponse",
        ["GuideViewCategory"] = "GuideViewResponse/properties/categories/items",
        ["UnmarkedSpan"] = "DocumentUnmarkedSpan",
        ["AuditWhole"] = "DocumentWholeOccurrence",
        ["AuditPart"] = "DocumentPartAssessment",
        ["FilterCount"] = "FilterResult/properties/total",
        ["SchemeContextFormats"] = "SchemeSummary/properties/contextFormats",
        // The OpenAPI component named ConceptRef is the guide-side reference
        // (id, category, value). The .NET ConceptRef is the fix reference.
        ["ConceptRef"] = "FixConceptReference",
        ["ContextComponentRef"] = "CtxComponentDescriptor",
        ["ContextDescription"] = "CtxDescribeResponseBody",
    };

    /// <summary>Contract properties a record deliberately does not carry, keyed by record and wire name.</summary>
    private static readonly Dictionary<(string Record, string Wire), string> Omitted = new()
    {
    };

    /// <summary>Record properties the contract does not declare, keyed by record and property name.</summary>
    private static readonly Dictionary<(string Record, string Property), string> Undeclared = new()
    {
    };

    /// <summary>
    /// Properties whose nullability deliberately differs from the contract's
    /// requiredness, keyed by record and wire name. Collections are not checked:
    /// the repository's convention reads an absent optional list as empty.
    /// </summary>
    private static readonly Dictionary<(string Record, string Wire), string> Requiredness = new()
    {
        [("ContextDocumentAuditResult", "responseProjection")] = DocumentProjection,
        [("ContextDocumentAccessAssessmentResult", "responseProjection")] = DocumentProjection,
        [("Term", "singular")] = TolerantWord,
        [("Term", "plural")] = TolerantWord,
    };

    /// <summary>
    /// Properties whose value type deliberately differs from the kind the
    /// contract declares, keyed by record and wire name.
    /// </summary>
    private static readonly Dictionary<(string Record, string Wire), string> Representation = new()
    {
        [("FilterResult", "categoryCounts")] =
            "The per-category counts and the total share one shape, so the one FilterCount record carries both.",
    };

    private const string DocumentProjection =
        "The contract declares responseProjection present only for document operations, and this is a document operation result, so the parser requires it.";

    private const string TolerantWord =
        "Read by the tolerant vocabulary reader, where an absent word is an empty string rather than a failure.";

    [Fact]
    public void EveryContractRecordCarriesTheShapeItsSchemaDeclares()
    {
        var schemas = LoadSchemas();
        var problems = new List<string>();

        foreach (var record in ContractRecords())
        {
            var source = ContractSource.TryGetValue(record.Name, out var mapped) ? mapped : record.Name;
            var schema = Resolve(schemas, source);
            if (schema is null)
            {
                problems.Add($"{record.Name}: no contract shape at components/schemas/{source}; map it in ContractSource.");
                continue;
            }

            // A union in the contract is an abstract record here, with one sealed
            // record per branch. Each branch record is held to its own component.
            if (record.IsAbstract)
            {
                problems.AddRange(UnionProblems(schemas, record, source, schema, ContractRecords(), ContractSource));
                continue;
            }

            var declared = Properties(schemas, schema);
            var carried = record
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var wire in declared.Where(wire => !carried.Contains(Pascal(wire))))
                if (!Omitted.ContainsKey((record.Name, wire)))
                    problems.Add($"{record.Name} lacks {Pascal(wire)}: the contract declares `{wire}` on {source}.");

            var required = Required(schemas, schema);
            foreach (var wire in declared)
            {
                var property = record.GetProperty(Pascal(wire), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property is null || IsCollection(property.PropertyType)) continue;
                var nullable = IsNullable(property);
                if (!required.Contains(wire) && !nullable && !Requiredness.ContainsKey((record.Name, wire)))
                    problems.Add($"{record.Name}.{property.Name} is non-nullable, but the contract leaves `{wire}` optional on {source}.");
            }

            var declaredClr = declared.Select(Pascal).ToHashSet(StringComparer.Ordinal);
            foreach (var property in carried.Where(property => !declaredClr.Contains(property)))
                if (!Undeclared.ContainsKey((record.Name, property)))
                    problems.Add($"{record.Name}.{property} is not declared by the contract on {source}.");
        }

        Assert.True(problems.Count == 0, "Records drifted from the vendored contract:\n  " + string.Join("\n  ", problems));
    }

    /// <summary>
    /// Each property has the kind of value its schema declares: a string or
    /// wire enum for a string, a list for an array, the record that models a
    /// referenced component when one exists, and opaque JSON only for a
    /// structure no record models. A member the contract requires is never
    /// nullable unless the contract admits null, opaque JSON and lists
    /// included, so a required member cannot read as absent.
    /// </summary>
    [Fact]
    public void EveryContractRecordPropertyHasTheTypeItsSchemaDeclares()
    {
        var schemas = LoadSchemas();
        var models = ComponentRecords();
        var problems = new List<string>();

        foreach (var record in ContractRecords().Where(record => !record.IsAbstract))
        {
            var source = ContractSource.TryGetValue(record.Name, out var mapped) ? mapped : record.Name;
            if (Resolve(schemas, source) is not { } schema) continue;
            var required = Required(schemas, schema);
            foreach (var wire in Properties(schemas, schema))
            {
                var property = record.GetProperty(Pascal(wire), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property is null) continue;
                var propertySchema = PropertySchema(schemas, schema, wire);
                if (TypeProblem(schemas, models, propertySchema, property.PropertyType, $"{source}/properties/{wire}") is { } problem
                    && !Representation.ContainsKey((record.Name, wire)))
                    problems.Add($"{record.Name}.{property.Name} {problem} (`{wire}` on {source}).");
                if (required.Contains(wire) && IsNullable(property) && !AdmitsNull(schemas, propertySchema)
                    && !Requiredness.ContainsKey((record.Name, wire)))
                    problems.Add($"{record.Name}.{property.Name} is nullable, but the contract requires `{wire}` on {source}.");
            }
        }

        Assert.True(problems.Count == 0, "Record property types drifted from the vendored contract:\n  " + string.Join("\n  ", problems));
    }

    [Fact]
    public void TypeCheck_ReportsAStringWhereTheContractNamesARecord()
    {
        var schemas = LoadSchemas();
        var reference = new YamlMappingNode { { "$ref", "#/components/schemas/RuleReference" } };
        Assert.Equal(
            "is String, but the contract's RuleReference is the record RuleReference",
            TypeProblem(schemas, ComponentRecords(), reference, typeof(string), "test"));
        Assert.Null(TypeProblem(schemas, ComponentRecords(), reference, typeof(RuleReference), "test"));
    }

    [Fact]
    public void TypeCheck_ReportsOpaqueJsonWhereARecordModelsTheComponent()
    {
        var schemas = LoadSchemas();
        var reference = new YamlMappingNode { { "$ref", "#/components/schemas/RuleReference" } };
        Assert.Equal(
            "is opaque JSON, but the record RuleReference models RuleReference",
            TypeProblem(schemas, ComponentRecords(), reference, typeof(System.Text.Json.JsonElement?), "test"));
    }

    [Fact]
    public void TypeCheck_ReportsOpaqueJsonForAComponentNotDeclaredOpaque()
    {
        var schemas = LoadSchemas();
        var reference = new YamlMappingNode { { "$ref", "#/components/schemas/ConceptModernization" } };
        var models = ComponentRecords();
        models.Remove("ConceptModernization");
        Assert.Equal(
            "is opaque JSON, but ConceptModernization is not a component this SDK keeps opaque",
            TypeProblem(schemas, models, reference, typeof(System.Text.Json.JsonElement), "test"));
    }

    [Fact]
    public void TypeCheck_ReportsTheRecordOfAnotherInlineObject()
    {
        var schemas = LoadSchemas();
        var categories = PropertySchema(schemas, Resolve(schemas, "GuideViewResponse")!, "categories");
        Assert.Equal(
            "holds items that is the record SchemeViewCategory, which models SchemeViewResponse/properties/categories/items, not GuideViewResponse/properties/categories/items",
            TypeProblem(schemas, ComponentRecords(), categories, typeof(IReadOnlyList<SchemeViewCategory>), "GuideViewResponse/properties/categories"));
        Assert.Null(TypeProblem(schemas, ComponentRecords(), categories, typeof(IReadOnlyList<GuideViewCategory>), "GuideViewResponse/properties/categories"));
    }

    /// <summary>
    /// Components this SDK carries as opaque JSON, each with the reason. Any other
    /// component a record property names must be read into the record that models it.
    /// </summary>
    private static readonly Dictionary<string, string> OpaqueComponents = new()
    {
        ["GuideRuleOutcome"] = "The rendered marking or concept references a guide rule contributes; the server has already applied them.",
        ["MatchNode"] = "The recursive guide-rule match DSL, carried through as authored rather than modeled node by node.",
        ["RuleWhen"] = "The recursive assertion-condition DSL, carried through as authored rather than modeled node by node.",
        ["CrosswalkTranslationResult"] = "The completed-or-forbidden translation union is read as JSON in this release; typing it is a cross-SDK decision.",
    };

    /// <summary>
    /// Records by the schema they model: a component name, or for an inline
    /// object the ContractSource path that maps them.
    /// </summary>
    private static Dictionary<string, Type> ComponentRecords()
    {
        var models = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var record in ContractRecords())
            models[ContractSource.TryGetValue(record.Name, out var mapped) ? mapped : record.Name] = record;
        return models;
    }

    /// <summary>
    /// What is wrong with <paramref name="clr"/> as the type of a value the
    /// schema describes, or null. <paramref name="at"/> is the schema path of
    /// <paramref name="node"/>, in the form ContractSource uses.
    /// </summary>
    private static string? TypeProblem(YamlMappingNode schemas, IReadOnlyDictionary<string, Type> models, YamlNode? node, Type clr, string at)
    {
        clr = Nullable.GetUnderlyingType(clr) ?? clr;
        if (node is not YamlMappingNode) return null;
        var component = ReferencedComponent(node);
        if (Deref(schemas, node) is not YamlMappingNode schema) return null;
        var opaque = clr == typeof(System.Text.Json.JsonElement);
        if (component is not null) at = component;

        if (component is not null && models.TryGetValue(component, out var model))
        {
            if (clr == model || clr.IsSubclassOf(model)) return null;
            return opaque
                ? $"is opaque JSON, but the record {model.Name} models {component}"
                : $"is {clr.Name}, but the contract's {component} is the record {model.Name}";
        }
        if (component is not null && opaque && !OpaqueComponents.ContainsKey(component)
            && !IsScalar(schemas, schema) && !AdmitsAnything(schema))
            return $"is opaque JSON, but {component} is not a component this SDK keeps opaque";

        foreach (var combinator in new[] { "anyOf", "oneOf" })
        {
            if (!schema.Children.TryGetValue(new YamlScalarNode(combinator), out var arms) || arms is not YamlSequenceNode choice)
                continue;
            var present = choice.Children.Where(arm => !IsNullSchema(schemas, arm)).ToList();
            if (present.Count == 1) return TypeProblem(schemas, models, present[0], clr, at);
            // A union of several shapes is opaque JSON, an abstract record, or a
            // string when every arm is one.
            if (opaque || clr.IsAbstract) return null;
            return present.All(arm => TypeProblem(schemas, models, arm, clr, at) is null)
                ? null
                : $"is {clr.Name}, but the contract declares a union of several shapes";
        }

        var kinds = SchemaTypes(schema);
        if (schema.Children.ContainsKey(new YamlScalarNode("enum")) || schema.Children.ContainsKey(new YamlScalarNode("const")))
            kinds.Add("string");
        if (schema.Children.ContainsKey(new YamlScalarNode("properties"))
            || schema.Children.ContainsKey(new YamlScalarNode("additionalProperties")))
            kinds.Add("object");
        if (kinds.Count != 1) return null;

        switch (kinds.Single())
        {
            case "string":
                return clr == typeof(string) || IsWireEnum(clr) ? null : $"is {clr.Name}, but the contract declares a string";
            case "boolean":
                return clr == typeof(bool) ? null : $"is {clr.Name}, but the contract declares a boolean";
            case "integer":
                return clr == typeof(int) || clr == typeof(long) ? null : $"is {clr.Name}, but the contract declares an integer";
            case "number":
                return clr == typeof(double) ? null : $"is {clr.Name}, but the contract declares a number";
            case "array":
                if (opaque) return null;
                if (ItemType(clr) is not { } item) return $"is {clr.Name}, but the contract declares an array";
                if (Nullable.GetUnderlyingType(item) is not null
                    && schema.Children.TryGetValue(new YamlScalarNode("items"), out var itemSchema)
                    && !AdmitsNull(schemas, itemSchema) && !AdmitsAnything(itemSchema))
                    return "holds nullable items, but the contract's items are never null";
                return schema.Children.TryGetValue(new YamlScalarNode("items"), out var items)
                    && TypeProblem(schemas, models, items, item, at + "/items") is { } inner
                    ? $"holds items that {inner}"
                    : null;
            case "object":
                if (opaque) return null;
                if (DictionaryValueType(clr) is { } value)
                    return schema.Children.TryGetValue(new YamlScalarNode("additionalProperties"), out var values)
                        && TypeProblem(schemas, models, values, value, at + "/additionalProperties") is { } entry
                        ? $"holds values that {entry}"
                        : null;
                if (clr.GetMethod("<Clone>$") is null) return $"is {clr.Name}, but the contract declares an object";
                var modeled = models.FirstOrDefault(pair => pair.Value == clr).Key;
                return modeled == at ? null : $"is the record {clr.Name}, which models {modeled}, not {at}";
            default:
                return null;
        }
    }

    private static string? ReferencedComponent(YamlNode node) =>
        node is YamlMappingNode map
            && map.Children.TryGetValue(new YamlScalarNode("$ref"), out var reference)
            && reference is YamlScalarNode { Value: { } target }
            && target.StartsWith("#/components/schemas/", StringComparison.Ordinal)
            ? target["#/components/schemas/".Length..]
            : null;

    private static HashSet<string> SchemaTypes(YamlMappingNode schema)
    {
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        if (schema.Children.TryGetValue(new YamlScalarNode("type"), out var type))
        {
            if (type is YamlScalarNode { Value: { } single }) kinds.Add(single);
            if (type is YamlSequenceNode several) kinds.UnionWith(several.Children.OfType<YamlScalarNode>().Select(t => t.Value!));
        }
        kinds.Remove("null");
        return kinds;
    }

    /// <summary>Whether a schema constrains nothing, as <c>{}</c> does, so any JSON value, null included, fits.</summary>
    private static bool AdmitsAnything(YamlNode node) =>
        node is YamlMappingNode schema
        && !schema.Children.Keys.OfType<YamlScalarNode>().Any(key => key.Value is "type" or "$ref" or "enum" or "const"
            or "anyOf" or "oneOf" or "allOf" or "properties" or "additionalProperties" or "items");

    /// <summary>Whether a schema describes a scalar: a string, number, boolean, enum or constant.</summary>
    private static bool IsScalar(YamlMappingNode schemas, YamlMappingNode schema)
    {
        var kinds = SchemaTypes(schema);
        if (schema.Children.ContainsKey(new YamlScalarNode("enum")) || schema.Children.ContainsKey(new YamlScalarNode("const"))) return true;
        return kinds.Count > 0 && kinds.All(kind => kind is "string" or "number" or "integer" or "boolean");
    }

    private static bool IsNullSchema(YamlMappingNode schemas, YamlNode arm) =>
        Deref(schemas, arm) is YamlMappingNode schema
        && schema.Children.TryGetValue(new YamlScalarNode("type"), out var type)
        && type is YamlScalarNode { Value: "null" };

    private static bool IsWireEnum(Type type) =>
        type.IsValueType && type.Namespace == "EzClassify" && type.GetMethod("ToWire", Type.EmptyTypes) is not null;

    private static Type? ItemType(Type type) =>
        type == typeof(string) ? null
        : type.GetInterfaces().Append(type)
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(candidate => candidate.GetGenericArguments()[0])
            .FirstOrDefault(argument => argument.IsGenericType is false || argument.GetGenericTypeDefinition() != typeof(KeyValuePair<,>));

    private static Type? DictionaryValueType(Type type) =>
        type.GetInterfaces().Append(type)
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
            .Select(candidate => candidate.GetGenericArguments()[1])
            .FirstOrDefault();

    [Fact]
    public void EveryAllowlistEntryStillDescribesARealDifference()
    {
        var schemas = LoadSchemas();
        var records = ContractRecords().ToDictionary(record => record.Name, StringComparer.Ordinal);
        var stale = new List<string>();

        foreach (var ((recordName, wire), _) in Omitted)
        {
            if (!records.TryGetValue(recordName, out var record)) { stale.Add($"Omitted {recordName}: no such record"); continue; }
            var source = ContractSource.TryGetValue(recordName, out var mapped) ? mapped : recordName;
            var declared = Properties(schemas, Resolve(schemas, source)!);
            if (!declared.Contains(wire) || record.GetProperty(Pascal(wire)) is not null)
                stale.Add($"Omitted ({recordName}, {wire})");
        }
        foreach (var ((recordName, property), _) in Undeclared)
        {
            if (!records.TryGetValue(recordName, out var record)) { stale.Add($"Undeclared {recordName}: no such record"); continue; }
            var source = ContractSource.TryGetValue(recordName, out var mapped) ? mapped : recordName;
            var declared = Properties(schemas, Resolve(schemas, source)!).Select(Pascal);
            if (record.GetProperty(property) is null || declared.Contains(property))
                stale.Add($"Undeclared ({recordName}, {property})");
        }

        foreach (var (recordName, _) in ContractSource)
            if (!records.ContainsKey(recordName))
                stale.Add($"ContractSource {recordName}: no such record");
        var models = ComponentRecords();
        foreach (var ((recordName, wire), _) in Representation)
        {
            if (!records.TryGetValue(recordName, out var record)) { stale.Add($"Representation {recordName}: no such record"); continue; }
            var source = ContractSource.TryGetValue(recordName, out var mapped) ? mapped : recordName;
            var property = record.GetProperty(Pascal(wire));
            if (property is null
                || TypeProblem(schemas, models, PropertySchema(schemas, Resolve(schemas, source)!, wire), property.PropertyType,
                    $"{source}/properties/{wire}") is null)
                stale.Add($"Representation ({recordName}, {wire})");
        }
        foreach (var ((recordName, wire), _) in Requiredness)
        {
            if (!records.TryGetValue(recordName, out var record)) { stale.Add($"Requiredness {recordName}: no such record"); continue; }
            var source = ContractSource.TryGetValue(recordName, out var mapped) ? mapped : recordName;
            var schema = Resolve(schemas, source)!;
            var property = record.GetProperty(Pascal(wire));
            if (property is null || IsCollection(property.PropertyType)
                || Required(schemas, schema).Contains(wire) == !IsNullable(property))
                stale.Add($"Requiredness ({recordName}, {wire})");
        }

        foreach (var (component, _) in OpaqueComponents)
            if (Resolve(schemas, component) is null || models.ContainsKey(component))
                stale.Add($"OpaqueComponents {component}: no such component, or a record now models it");

        Assert.True(stale.Count == 0, "Allowlist entries no longer apply; remove them:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// Hold an abstract record to the union its contract shape declares: one
    /// derived record per branch component and none besides, and on the base
    /// only members every branch declares. A derived record named differently
    /// from its branch component is mapped in <paramref name="sources"/>, as
    /// any record is in <see cref="ContractSource"/>.
    /// </summary>
    private static IEnumerable<string> UnionProblems(
        YamlMappingNode schemas, Type record, string source, YamlMappingNode schema,
        IEnumerable<Type> records, IReadOnlyDictionary<string, string> sources)
    {
        var branches = new List<string>();
        foreach (var combinator in new[] { "anyOf", "oneOf" })
            if (schema.Children.TryGetValue(new YamlScalarNode(combinator), out var arms) && arms is YamlSequenceNode sequence)
                foreach (var arm in sequence.Children.OfType<YamlMappingNode>())
                    if (arm.Children.TryGetValue(new YamlScalarNode("$ref"), out var reference)
                        && reference is YamlScalarNode { Value: { } target }
                        && target.StartsWith("#/components/schemas/", StringComparison.Ordinal))
                        branches.Add(target["#/components/schemas/".Length..]);
        if (branches.Count == 0)
        {
            yield return $"{record.Name} is abstract, but {source} is not a union of components.";
            yield break;
        }

        var derived = records
            .Where(type => type.BaseType == record)
            .Select(type => sources.TryGetValue(type.Name, out var mapped) ? mapped : type.Name)
            .ToList();
        foreach (var branch in branches.Where(branch => !derived.Contains(branch)))
            yield return $"{record.Name} has no derived record for the {branch} branch of {source}.";
        foreach (var name in derived.Where(name => !branches.Contains(name)))
            yield return $"{name} derives from {record.Name}, but {source} has no {name} branch.";

        var shared = record
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name);
        foreach (var name in shared)
            foreach (var branch in branches)
                if (Resolve(schemas, branch) is not { } arm || !Properties(schemas, arm).Select(Pascal).Contains(name))
                    yield return $"{record.Name}.{name} is on the union base, but the {branch} branch does not declare it.";
    }

    // Negative controls for the union check: each shape below is wrong in one
    // way against the contract's UnrepresentableValue, and the check must say
    // so. The branch records carry their own names, mapped to the branch
    // components, so they never shadow the generated records.

    private static readonly Dictionary<string, string> BranchSource = new()
    {
        ["FactBranch"] = "UnrepresentableFact",
        ["OutputBranch"] = "UnrepresentableOutput",
        ["SurfaceBranch"] = "UnrepresentableSurface",
    };

    private static class MissingBranch
    {
        public abstract record Union
        {
            public abstract string? Detail { get; init; }
        }

        public sealed record FactBranch(string ConceptId, string Reason, string? Detail = null) : Union;
    }

    private static class StrayBranch
    {
        public abstract record Union;

        public sealed record FactBranch(string ConceptId, string Reason, string? Detail = null) : Union;

        public sealed record OutputBranch(string Output, string Reason, string? Detail = null) : Union;

        public sealed record SurfaceBranch(string Surface) : Union;
    }

    private static class UnsharedMember
    {
        public abstract record Union
        {
            public abstract string ConceptId { get; init; }
        }

        public sealed record FactBranch(string ConceptId, string Reason, string? Detail = null) : Union;

        public sealed record OutputBranch(string ConceptId, string Output, string Reason, string? Detail = null) : Union;
    }

    [Fact]
    public void UnionCheck_ReportsABranchWithNoRecord()
    {
        var problems = UnionProblemsFor(typeof(MissingBranch.Union), typeof(MissingBranch).GetNestedTypes());
        Assert.Equal(new[] { "Union has no derived record for the UnrepresentableOutput branch of UnrepresentableValue." }, problems);
    }

    [Fact]
    public void UnionCheck_ReportsARecordThatIsNoBranch()
    {
        var problems = UnionProblemsFor(typeof(StrayBranch.Union), typeof(StrayBranch).GetNestedTypes());
        Assert.Equal(new[] { "UnrepresentableSurface derives from Union, but UnrepresentableValue has no UnrepresentableSurface branch." }, problems);
    }

    [Fact]
    public void UnionCheck_ReportsABaseMemberABranchDoesNotDeclare()
    {
        var problems = UnionProblemsFor(typeof(UnsharedMember.Union), typeof(UnsharedMember).GetNestedTypes());
        Assert.Equal(new[] { "Union.ConceptId is on the union base, but the UnrepresentableOutput branch does not declare it." }, problems);
    }

    private static List<string> UnionProblemsFor(Type union, IEnumerable<Type> records)
    {
        var schemas = LoadSchemas();
        return UnionProblems(schemas, union, "UnrepresentableValue", Resolve(schemas, "UnrepresentableValue")!, records, BranchSource)
            .ToList();
    }

    private static IEnumerable<Type> ContractRecords() =>
        typeof(CoverageReport).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == "EzClassify" && type.GetMethod("<Clone>$") is not null)
            .OrderBy(type => type.Name, StringComparer.Ordinal);

    private static YamlMappingNode LoadSchemas()
    {
        var yaml = new YamlStream();
        using (var reader = new StreamReader(Path.Combine(FindRepoRoot(), "vendor", "spec", "openapi.yaml")))
            yaml.Load(reader);
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        return (YamlMappingNode)((YamlMappingNode)root["components"])["schemas"];
    }

    /// <summary>Follow a <c>Component[/key/...]</c> path to a schema node, or null.</summary>
    private static YamlMappingNode? Resolve(YamlMappingNode schemas, string path)
    {
        YamlNode? node = schemas;
        foreach (var segment in path.Split('/'))
        {
            if (node is not YamlMappingNode map || !map.Children.TryGetValue(new YamlScalarNode(segment), out node))
                return null;
            node = Deref(schemas, node);
        }
        return node as YamlMappingNode;
    }

    private static YamlNode Deref(YamlMappingNode schemas, YamlNode node)
    {
        while (node is YamlMappingNode map
            && map.Children.TryGetValue(new YamlScalarNode("$ref"), out var reference)
            && reference is YamlScalarNode { Value: { } target }
            && target.StartsWith("#/components/schemas/", StringComparison.Ordinal)
            && schemas.Children.TryGetValue(new YamlScalarNode(target["#/components/schemas/".Length..]), out var next))
        {
            node = next;
        }
        return node;
    }

    /// <summary>The wire property names an object schema declares, across allOf, anyOf and oneOf arms.</summary>
    private static HashSet<string> Properties(YamlMappingNode schemas, YamlMappingNode schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (schema.Children.TryGetValue(new YamlScalarNode("properties"), out var properties) && properties is YamlMappingNode map)
            foreach (var key in map.Children.Keys.OfType<YamlScalarNode>())
                names.Add(key.Value!);
        foreach (var combinator in new[] { "allOf", "anyOf", "oneOf" })
            if (schema.Children.TryGetValue(new YamlScalarNode(combinator), out var arms) && arms is YamlSequenceNode sequence)
                foreach (var arm in sequence.Children.Select(arm => Deref(schemas, arm)).OfType<YamlMappingNode>())
                    names.UnionWith(Properties(schemas, arm));
        return names;
    }

    /// <summary>
    /// The wire properties an object schema requires: its own list and every
    /// allOf arm's, plus what every anyOf or oneOf arm requires alike.
    /// </summary>
    private static HashSet<string> Required(YamlMappingNode schemas, YamlMappingNode schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (schema.Children.TryGetValue(new YamlScalarNode("required"), out var list) && list is YamlSequenceNode sequence)
            foreach (var item in sequence.Children.OfType<YamlScalarNode>())
                names.Add(item.Value!);
        if (schema.Children.TryGetValue(new YamlScalarNode("allOf"), out var all) && all is YamlSequenceNode allArms)
            foreach (var arm in allArms.Children.Select(arm => Deref(schemas, arm)).OfType<YamlMappingNode>())
                names.UnionWith(Required(schemas, arm));
        foreach (var combinator in new[] { "anyOf", "oneOf" })
        {
            if (!schema.Children.TryGetValue(new YamlScalarNode(combinator), out var arms) || arms is not YamlSequenceNode choice)
                continue;
            HashSet<string>? common = null;
            foreach (var arm in choice.Children.Select(arm => Deref(schemas, arm)).OfType<YamlMappingNode>())
            {
                var armRequired = Required(schemas, arm);
                if (common is null) common = armRequired;
                else common.IntersectWith(armRequired);
            }
            if (common is not null) names.UnionWith(common);
        }
        return names;
    }

    /// <summary>The schema of one declared property, looked up across allOf, anyOf and oneOf arms.</summary>
    private static YamlNode? PropertySchema(YamlMappingNode schemas, YamlMappingNode schema, string wire)
    {
        if (schema.Children.TryGetValue(new YamlScalarNode("properties"), out var properties) && properties is YamlMappingNode map
            && map.Children.TryGetValue(new YamlScalarNode(wire), out var found))
            return found;
        foreach (var combinator in new[] { "allOf", "anyOf", "oneOf" })
            if (schema.Children.TryGetValue(new YamlScalarNode(combinator), out var arms) && arms is YamlSequenceNode sequence)
                foreach (var arm in sequence.Children.Select(arm => Deref(schemas, arm)).OfType<YamlMappingNode>())
                    if (PropertySchema(schemas, arm, wire) is { } inArm) return inArm;
        return null;
    }

    /// <summary>Whether a property schema lets the value be JSON null.</summary>
    private static bool AdmitsNull(YamlMappingNode schemas, YamlNode? node)
    {
        if (Deref(schemas, node ?? new YamlMappingNode()) is not YamlMappingNode schema) return false;
        if (schema.Children.TryGetValue(new YamlScalarNode("nullable"), out var flag) && flag is YamlScalarNode { Value: "true" })
            return true;
        if (schema.Children.TryGetValue(new YamlScalarNode("type"), out var type))
        {
            if (type is YamlScalarNode { Value: "null" }) return true;
            if (type is YamlSequenceNode types && types.Children.OfType<YamlScalarNode>().Any(t => t.Value == "null")) return true;
        }
        foreach (var combinator in new[] { "anyOf", "oneOf" })
            if (schema.Children.TryGetValue(new YamlScalarNode(combinator), out var arms) && arms is YamlSequenceNode sequence
                && sequence.Children.Any(arm => AdmitsNull(schemas, arm)))
                return true;
        return false;
    }

    private static readonly NullabilityInfoContext Nullability = new();

    private static bool IsNullable(PropertyInfo property) =>
        Nullable.GetUnderlyingType(property.PropertyType) is not null
        || (!property.PropertyType.IsValueType && Nullability.Create(property).ReadState == NullabilityState.Nullable);

    private static bool IsCollection(Type type) =>
        type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    private static string Pascal(string wire) => char.ToUpperInvariant(wire[0]) + wire[1..];

    private static string FindRepoRoot()
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
