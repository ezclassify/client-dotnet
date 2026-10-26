using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace EzClassify.Tests;

/// <summary>
/// The wire enums are extensible structs over their raw token (the Azure SDK
/// pattern). Every one of them must keep a token byte for byte whether or not
/// this SDK declares it, compare tokens ordinally, parse strictly, and refuse to
/// encode <c>default</c>, which carries no token. The sweep runs over every
/// exported wire-enum struct, so a new one is covered the day it is added.
/// </summary>
public class WireEnumsTests
{
    private const string Unseen = "value-added-by-a-newer-server";

    /// <summary>Every exported extensible wire enum in the contract assembly.</summary>
    internal static IReadOnlyList<Type> ExtensibleEnums { get; } = typeof(Confidence).Assembly.GetExportedTypes()
        .Where(type => type.IsValueType && !type.IsEnum
            && type.GetProperty("IsKnown", BindingFlags.Public | BindingFlags.Instance) is not null)
        .OrderBy(type => type.Name, StringComparer.Ordinal)
        .ToList();

    /// <summary>The declared values of one extensible wire enum, read off its static properties.</summary>
    internal static IReadOnlyList<object> KnownValues(Type type) => type
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(property => property.PropertyType == type)
        .Select(property => property.GetValue(null)!)
        .ToList();

    public static IEnumerable<object[]> EnumNames() => ExtensibleEnums.Select(type => new object[] { type.Name });

    private static Type Named(string name) => ExtensibleEnums.Single(type => type.Name == name);

    private static object Construct(Type type, string token) => Activator.CreateInstance(type, token)!;

    private static object? InvokeStatic(Type type, string name, params object?[] args)
    {
        try
        {
            return type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null,
                args.Select(arg => arg?.GetType() ?? typeof(string)).ToArray(), null)!.Invoke(null, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }

    private static string ToWire(object value)
    {
        try
        {
            return (string)value.GetType().GetMethod("ToWire", Type.EmptyTypes)!.Invoke(value, null)!;
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }

    private static bool IsKnown(object value) => (bool)value.GetType().GetProperty("IsKnown")!.GetValue(value)!;

    private static (bool Ok, object Result) TryParse(Type type, string? token)
    {
        var args = new object?[] { token, null };
        var method = type.GetMethod("TryParse", new[] { typeof(string), type.MakeByRefType() })!;
        var ok = (bool)method.Invoke(null, args)!;
        return (ok, args[1]!);
    }

    [Fact]
    public void TheSweepSeesEveryWireEnum()
    {
        Assert.Equal(26, ExtensibleEnums.Count);
        Assert.Contains(typeof(Confidence), ExtensibleEnums);
        Assert.Contains(typeof(ArtifactKind), ExtensibleEnums);
        Assert.DoesNotContain(typeof(RenderPreservation), ExtensibleEnums);
    }

    [Theory]
    [MemberData(nameof(EnumNames))]
    public void DeclaredValuesRoundTripThroughTheirTokens(string name)
    {
        var type = Named(name);
        var known = KnownValues(type);
        Assert.NotEmpty(known);
        foreach (var value in known)
        {
            var token = ToWire(value);
            Assert.True(IsKnown(value), $"{name}.{token} is not known");
            Assert.Equal(token, value.ToString());
            Assert.Equal(value, InvokeStatic(type, "Parse", token));
            Assert.Equal(value, Construct(type, token));
            var (ok, parsed) = TryParse(type, token);
            Assert.True(ok);
            Assert.Equal(value, parsed);
        }
        Assert.Equal(known.Count, known.Select(ToWire).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(EnumNames))]
    public void AnUnseenTokenIsKeptExactlyAndParseRefusesIt(string name)
    {
        var type = Named(name);
        foreach (var token in new[] { Unseen, "Mixed-Case Token", "" })
        {
            var value = Construct(type, token);

            Assert.False(IsKnown(value));
            Assert.Equal(token, value.ToString());
            Assert.Equal(token, ToWire(value));
            Assert.Equal(value, Construct(type, token));
            var refused = Assert.Throws<ArgumentException>(() => InvokeStatic(type, "Parse", token));
            Assert.Contains(token, refused.Message, StringComparison.Ordinal);
            var (ok, parsed) = TryParse(type, token);
            Assert.False(ok);
            Assert.Equal(Activator.CreateInstance(type), parsed);
        }
    }

    [Theory]
    [MemberData(nameof(EnumNames))]
    public void TokensCompareOrdinally(string name)
    {
        var type = Named(name);
        foreach (var value in KnownValues(type))
        {
            var upper = Construct(type, ToWire(value).ToUpperInvariant());
            Assert.NotEqual(value, upper);
            Assert.False(IsKnown(upper));
            Assert.Equal(value.GetHashCode(), Construct(type, ToWire(value)).GetHashCode());
            Assert.True((bool)type.GetMethod("op_Equality")!.Invoke(null, new[] { value, Construct(type, ToWire(value)) })!);
            Assert.True((bool)type.GetMethod("op_Inequality")!.Invoke(null, new[] { value, upper })!);
        }
    }

    [Theory]
    [MemberData(nameof(EnumNames))]
    public void DefaultCarriesNoTokenAndCannotBeSent(string name)
    {
        var type = Named(name);
        var empty = Activator.CreateInstance(type)!;

        Assert.False(IsKnown(empty));
        Assert.Equal("", empty.ToString());
        Assert.Throws<InvalidOperationException>(() => ToWire(empty));
        Assert.NotEqual(empty, Construct(type, ""));
        Assert.DoesNotContain(empty, KnownValues(type));
        Assert.Throws<ArgumentNullException>(() => InvokeStatic(type, "Parse", new object?[] { null }));
        Assert.False(TryParse(type, null).Ok);
        var ctor = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(type, new object?[] { null }));
        Assert.IsType<ArgumentNullException>(ctor.InnerException);
    }

    [Theory]
    [MemberData(nameof(EnumNames))]
    public void SystemTextJsonRoundTripsTheRawToken(string name)
    {
        var type = Named(name);
        foreach (var token in new[] { ToWire(KnownValues(type)[0]), Unseen })
        {
            var value = Construct(type, token);
            var json = JsonSerializer.Serialize(value, type);

            Assert.Equal(JsonSerializer.Serialize(token), json);
            Assert.Equal(value, JsonSerializer.Deserialize(json, type));
        }

        var nullable = typeof(Nullable<>).MakeGenericType(type);
        Assert.Equal("null", JsonSerializer.Serialize((object?)null, nullable));
        Assert.Null(JsonSerializer.Deserialize("null", nullable));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("7", type));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(Activator.CreateInstance(type), type));
    }

    [Fact]
    public void KnownValuesReadNaturallyAtTheCallSite()
    {
        Assert.Equal(Confidence.Certain, Confidence.Parse("certain"));
        Assert.Equal("xml-attributes", FormatSyntaxType.XmlAttributes.ToWire());
        Assert.Equal("resource-limit", ScanStatus.ResourceLimit.ToString());
        Assert.True(ArtifactKind.TryParse("crosswalk", out var crosswalk));
        Assert.Equal(ArtifactKind.Crosswalk, crosswalk);
        Assert.False(new Confidence("probable").IsKnown);
        Assert.Throws<ArgumentException>(() => RenderFormatMode.Parse("stacked"));
    }

    [Fact]
    public void RenderPreservationTravelsOnlyInRequestsAndStaysAClosedEnum()
    {
        Assert.Equal("projection", RenderPreservation.Projection.ToWire());
        Assert.Equal("full", RenderPreservation.Full.ToWire());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((RenderPreservation)99).ToWire());
    }
}
