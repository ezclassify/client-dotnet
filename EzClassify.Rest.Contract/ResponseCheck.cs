using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace EzClassify;

/// <summary>
/// One node of a response shape: what a JSON value must be, and for an object
/// which members it must carry. The generated <see cref="ResponseShapes"/> table
/// is built from these; nothing else constructs them.
/// </summary>
internal sealed class ShapeNode
{
    internal enum NodeKind { Ref, Obj, Arr, Map, Any }

    internal NodeKind Kind { get; }

    /// <summary>The component a <see cref="NodeKind.Ref"/> names.</summary>
    internal string Name { get; } = "";

    /// <summary>The members an object must carry, in the order the contract lists them.</summary>
    internal IReadOnlyList<string> Required { get; } = Array.Empty<string>();

    /// <summary>The required members whose value may be JSON null.</summary>
    internal ISet<string> Nullable { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The members that hold a container, with the shape each holds.</summary>
    internal IReadOnlyList<(string Name, ShapeNode Shape)> Members { get; } = Array.Empty<(string, ShapeNode)>();

    /// <summary>The shape of an array's items or a map's values.</summary>
    internal ShapeNode? Item { get; }

    /// <summary>Whether a union also admits a scalar.</summary>
    internal bool AcceptsScalar { get; }

    /// <summary>The alternatives of a union, each with the string-valued members that pick it out.</summary>
    internal IReadOnlyList<(ShapeNode Shape, (string Name, string Value)[]? Discriminators)> Alternatives { get; }
        = Array.Empty<(ShapeNode, (string, string)[]?)>();

    private ShapeNode(NodeKind kind) => Kind = kind;

    private ShapeNode(string name) : this(NodeKind.Ref) => Name = name;

    private ShapeNode(string required, string? nullable, (string, ShapeNode)[]? members)
        : this(NodeKind.Obj)
    {
        Required = Split(required);
        Nullable = new HashSet<string>(Split(nullable), StringComparer.Ordinal);
        Members = members ?? Array.Empty<(string, ShapeNode)>();
    }

    private ShapeNode(NodeKind kind, ShapeNode? item) : this(kind) => Item = item;

    private ShapeNode(bool acceptsScalar, (ShapeNode, (string, string)[]?)[] alternatives) : this(NodeKind.Any)
    {
        AcceptsScalar = acceptsScalar;
        Alternatives = alternatives;
    }

    private static string[] Split(string? names) =>
        string.IsNullOrEmpty(names) ? Array.Empty<string>() : names!.Split(' ');

    internal static ShapeNode Ref(string name) => new(name);

    internal static ShapeNode Obj(string required, string? nullable = null, (string, ShapeNode)[]? members = null)
        => new(required, nullable, members);

    internal static ShapeNode Arr(ShapeNode? items) => new(NodeKind.Arr, items);

    internal static ShapeNode Map(ShapeNode values) => new(NodeKind.Map, values);

    internal static ShapeNode Any(bool acceptsScalar, params (ShapeNode, (string, string)[]?)[] alternatives)
        => new(acceptsScalar, alternatives);
}

/// <summary>
/// Refuses a response body that lacks a member the contract requires.
/// </summary>
/// <remarks>
/// The check walks <see cref="ResponseShapes"/>, which is generated from the
/// OpenAPI <c>required</c> lists, so what counts as required is the contract's
/// answer and not this assembly's. It runs before a parser reads anything: a
/// parser that fell back to an empty collection or a default for an absent
/// member would turn a malformed answer into a plausible one, and a client that
/// caches answers would keep it. A member the contract marks optional is never
/// judged here, and a value of the wrong scalar type is left to the parser that
/// reads it. Every refusal is a <see cref="JsonException"/> whose
/// <see cref="JsonException.Path"/> names the member, such as
/// <c>$.result.assessment.emitted</c>.
/// </remarks>
internal static class ResponseCheck
{
    /// <summary>Require the body to satisfy the component schema, at any depth.</summary>
    internal static void Require(string component, JsonElement value)
    {
        if (!Walk(ShapeNode.Ref(component), value, out var suffix, out var reason)) throw Refusal(suffix, reason);
    }

    /// <summary>Require the body to be an array whose every item satisfies the component schema.</summary>
    internal static void RequireEach(string component, JsonElement value)
    {
        if (!Walk(ShapeNode.Arr(ShapeNode.Ref(component)), value, out var suffix, out var reason))
            throw Refusal(suffix, reason);
    }

    private const string Missing = "is missing";

    private static JsonException Refusal(string suffix, string reason)
    {
        var path = "$" + suffix;
        var message = reason == Missing
            ? $"Response is missing required member {path}."
            : $"Response member {path} {reason}.";
        return new JsonException(message, path, null, null);
    }

    private static ShapeNode? Resolve(ShapeNode reference)
    {
        if (ResponseShapes.Components.TryGetValue(reference.Name, out var shape)) return shape;
        throw new ArgumentException($"The contract declares no response shape for {reference.Name}.", nameof(reference));
    }

    private static bool IsScalar(JsonElement value) =>
        value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array);

    /// <summary>Whether the value has the container kind the node holds, so a walk can judge its members.</summary>
    private static bool Matches(ShapeNode node, JsonElement value)
    {
        switch (node.Kind)
        {
            case ShapeNode.NodeKind.Ref: return Matches(Resolve(node)!, value);
            case ShapeNode.NodeKind.Obj:
            case ShapeNode.NodeKind.Map: return value.ValueKind == JsonValueKind.Object;
            case ShapeNode.NodeKind.Arr: return value.ValueKind == JsonValueKind.Array;
            default:
                return IsScalar(value) ? node.AcceptsScalar : node.Alternatives.Any(a => Matches(a.Shape, value));
        }
    }

    private static string KindReason(ShapeNode node) => node.Kind switch
    {
        ShapeNode.NodeKind.Ref => KindReason(Resolve(node)!),
        ShapeNode.NodeKind.Arr => "must be an array",
        ShapeNode.NodeKind.Any => "does not match any shape the contract allows",
        _ => "must be an object",
    };

    private static bool Walk(ShapeNode node, JsonElement value, out string suffix, out string reason)
    {
        suffix = "";
        reason = "";
        if (node.Kind == ShapeNode.NodeKind.Ref) return Walk(Resolve(node)!, value, out suffix, out reason);
        if (!Matches(node, value))
        {
            reason = KindReason(node);
            return false;
        }
        switch (node.Kind)
        {
            case ShapeNode.NodeKind.Obj: return WalkObject(node, value, out suffix, out reason);
            case ShapeNode.NodeKind.Arr: return WalkItems(node.Item, value, out suffix, out reason);
            case ShapeNode.NodeKind.Map: return WalkEntries(node.Item!, value, out suffix, out reason);
            default: return WalkUnion(node, value, out suffix, out reason);
        }
    }

    private static bool WalkObject(ShapeNode node, JsonElement value, out string suffix, out string reason)
    {
        suffix = "";
        reason = "";
        foreach (var name in node.Required)
        {
            if (value.TryGetProperty(name, out var member)
                && (member.ValueKind != JsonValueKind.Null || node.Nullable.Contains(name)))
                continue;
            suffix = Segment(name);
            reason = Missing;
            return false;
        }
        foreach (var (name, shape) in node.Members)
        {
            if (!value.TryGetProperty(name, out var member) || member.ValueKind == JsonValueKind.Null) continue;
            // An optional member of the wrong kind is the parser's to tolerate;
            // a required one was promised.
            if (!Matches(shape, member) && !node.Required.Contains(name)) continue;
            if (Walk(shape, member, out var inner, out reason))
                continue;
            suffix = Segment(name) + inner;
            return false;
        }
        return true;
    }

    private static bool WalkItems(ShapeNode? items, JsonElement array, out string suffix, out string reason)
    {
        suffix = "";
        reason = "";
        if (items is null) return true;
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (!Walk(items, item, out var inner, out reason))
            {
                suffix = $"[{index}]{inner}";
                return false;
            }
            index++;
        }
        return true;
    }

    private static bool WalkEntries(ShapeNode values, JsonElement map, out string suffix, out string reason)
    {
        suffix = "";
        reason = "";
        foreach (var entry in map.EnumerateObject())
        {
            if (Walk(values, entry.Value, out var inner, out reason)) continue;
            suffix = Segment(entry.Name) + inner;
            return false;
        }
        return true;
    }

    private static bool WalkUnion(ShapeNode node, JsonElement value, out string suffix, out string reason)
    {
        suffix = "";
        reason = "";
        if (IsScalar(value)) return true;
        var candidates = node.Alternatives.Where(a => Matches(a.Shape, value)).ToList();
        // A discriminated alternative is judged only when the value names it. A
        // value naming none of them is a variant this client has not met, which
        // an open-response contract leaves to the parser; a value that names no
        // discriminator at all is tried against every alternative, each of which
        // requires its own.
        var named = candidates.Where(a => Discrimination(a.Discriminators, value) == Named.Yes).ToList();
        if (named.Count > 0) candidates = named;
        else
        {
            candidates = candidates
                .Where(a => a.Discriminators is null || Discrimination(a.Discriminators, value) == Named.Absent)
                .ToList();
            if (candidates.Count == 0) return true;
        }
        var failed = false;
        foreach (var candidate in candidates)
        {
            if (Walk(candidate.Shape, value, out var inner, out var why)) return true;
            if (failed) continue;
            failed = true;
            suffix = inner;
            reason = why;
        }
        return false;
    }

    private enum Named { Yes, No, Absent }

    private static Named Discrimination((string Name, string Value)[]? discriminators, JsonElement value)
    {
        if (discriminators is null) return Named.No;
        var absent = false;
        foreach (var (name, expected) in discriminators)
        {
            if (!value.TryGetProperty(name, out var member) || member.ValueKind != JsonValueKind.String)
            {
                absent = true;
                continue;
            }
            if (member.GetString() != expected) return Named.No;
        }
        return absent ? Named.Absent : Named.Yes;
    }

    private static string Segment(string name)
    {
        foreach (var c in name)
            if (!(char.IsLetterOrDigit(c) || c == '_'))
                return $"['{name.Replace("'", "\\'")}']";
        return "." + name;
    }
}
