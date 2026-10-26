using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace EzClassify;

/// <summary>
/// Shared JSON reading primitives for the response parsers.
/// </summary>
/// <remarks>
/// A member the contract requires is read with a <c>Required*</c> or
/// <c>Strict*</c> reader, which throws <see cref="JsonException"/> when the
/// member is absent, null, or the wrong kind. A member the contract marks
/// optional is read with an <c>Optional*</c> or <c>Tolerant*</c> reader, which
/// degrades an absent, null, or wrong-typed value to the member's empty default.
/// Which members the contract requires is not decided by the parsers:
/// <see cref="ResponseCheck"/> refuses a body that lacks any of them, at any
/// depth, before a parser reads it, and these readers keep a parser that is
/// handed an element directly from filling a default in.
/// </remarks>
internal static class JsonRead
{
    /// <summary>Read a property that the contract requires to be present.</summary>
    internal static JsonElement RequiredProperty(JsonElement parent, string key)
    {
        if (!parent.TryGetProperty(key, out var value)) throw new JsonException($"Missing required property: {key}");
        return value;
    }

    /// <summary>Reject anything but a JSON object.</summary>
    internal static void RequireObject(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) throw new JsonException($"Expected {name} object");
    }

    /// <summary>Reject anything but a JSON array.</summary>
    internal static void RequireArray(JsonElement el, string what)
    {
        if (el.ValueKind != JsonValueKind.Array) throw new JsonException($"Expected an array of {what} objects");
    }

    /// <summary>Read a required string; an absent, null, or non-string value throws.</summary>
    internal static string RequiredString(JsonElement parent, string key)
    {
        var value = RequiredProperty(parent, key);
        if (value.ValueKind != JsonValueKind.String) throw new JsonException($"{key} must be a string");
        return value.GetString()!;
    }

    /// <summary>Read a required integer; an absent, null, or non-integer value throws.</summary>
    internal static int RequiredInt(JsonElement parent, string key)
    {
        var value = RequiredProperty(parent, key);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            throw new JsonException($"{key} must be an integer");
        return number;
    }

    /// <summary>
    /// Read a verdict boolean the contract requires. A verdict decoded to false
    /// because the member was missing would be a wrong answer, so absent, null
    /// and wrong-typed values throw.
    /// </summary>
    internal static bool RequiredBool(JsonElement parent, string key)
    {
        var value = RequiredProperty(parent, key);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new JsonException($"{key} must be a boolean"),
        };
    }

    /// <summary>Read a required array of strings; a missing key or a non-string element throws.</summary>
    internal static IReadOnlyList<string> StrictStringList(JsonElement parent, string key)
    {
        var values = new List<string>();
        var arr = RequiredProperty(parent, key);
        if (arr.ValueKind != JsonValueKind.Array) throw new JsonException($"{key} must be an array");
        foreach (var value in arr.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String) throw new JsonException($"{key} must contain strings");
            values.Add(value.GetString()!);
        }
        return values.AsReadOnly();
    }

    /// <summary>
    /// Read a required array, each element through <paramref name="parse"/>; an
    /// absent, null, or non-array value throws.
    /// </summary>
    internal static IReadOnlyList<T> RequiredList<T>(JsonElement parent, string key, Func<JsonElement, T> parse)
    {
        var array = RequiredProperty(parent, key);
        if (array.ValueKind != JsonValueKind.Array) throw new JsonException($"{key} must be an array");
        return ElementList(array, parse);
    }

    /// <summary>Read every element of an array through <paramref name="parse"/> into a list no caller can change.</summary>
    internal static IReadOnlyList<T> ElementList<T>(JsonElement array, Func<JsonElement, T> parse)
    {
        if (array.ValueKind != JsonValueKind.Array) throw new JsonException("Expected an array");
        var values = new List<T>();
        foreach (var value in array.EnumerateArray()) values.Add(parse(value));
        return values.AsReadOnly();
    }

    /// <summary>Read an optional string: absent, null, or wrong-typed is null.</summary>
    internal static string? OptionalString(JsonElement parent, string key) =>
        parent.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// Read an optional array of strings under the strict contract: absent or
    /// JSON null yields null, but a present value that is not an array of
    /// strings is a contract violation and throws.
    /// </summary>
    internal static IReadOnlyList<string>? StrictOptionalStringList(JsonElement parent, string key)
        => parent.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null
            ? StrictStringList(parent, key)
            : null;

    /// <summary>
    /// Read an optional array of strings under the tolerant contract: anything
    /// that is not an array yields null, and a wrong-typed element degrades to
    /// an empty string.
    /// </summary>
    internal static IReadOnlyList<string>? TolerantOptionalStringList(JsonElement parent, string key)
    {
        if (!parent.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<string>();
        foreach (var el in arr.EnumerateArray())
            list.Add(el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "");
        return list.AsReadOnly();
    }

    /// <summary>
    /// Read an optional string-valued object under the tolerant contract:
    /// anything that is not an object yields null, and a wrong-typed value
    /// degrades to an empty string.
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? TolerantOptionalStringMap(JsonElement parent, string key)
    {
        if (!parent.TryGetProperty(key, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return null;
        var map = new Dictionary<string, string>();
        foreach (var property in obj.EnumerateObject())
            map[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : "";
        return new ReadOnlyDictionary<string, string>(map);
    }

    /// <summary>
    /// Read an optional array of <typeparamref name="T"/> under the tolerant
    /// contract: anything that is not an array yields null.
    /// </summary>
    internal static IReadOnlyList<T>? TolerantOptionalList<T>(JsonElement parent, string key, Func<JsonElement, T> parse)
        => parent.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? ElementList(arr, parse)
            : null;
}
