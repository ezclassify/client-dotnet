using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

/// <summary>
/// Runs the shared cache conformance plans (<c>vendor/spec/cache/v1/plans.json</c>,
/// whose contract is <c>CACHE-SEMANTICS.md</c> beside it) against this SDK's public
/// client. The plans are the contract: nothing here restates a cache rule, so the
/// SDK and its tests cannot agree with each other by sharing a mistake.
///
/// <para>Every capability the plans ask of a harness is supplied except two the
/// plans allow a port to declare absent. <c>explicitNullMember</c>: this SDK's
/// request members are optional parameters, and an unset one is always omitted
/// from the body, so no public call can send a null member.
/// <c>sharedRouteOperations</c>: CACHE-SEMANTICS.md binds C05 only to a port
/// with two operations over the same route and body. The two public calls over
/// the check route post different bodies, assertions from
/// <c>Access.CheckAsync</c> and a marking from <c>Access.CheckByMarkingAsync</c>,
/// so no two public operations share a route and body.</para>
/// </summary>
public class CachePlanTests
{
    /// <summary>
    /// The capabilities this SDK declares not applicable; the plans file must
    /// allow each one to be declared absent.
    /// </summary>
    private static readonly IReadOnlySet<string> NotApplicable = new HashSet<string>(StringComparer.Ordinal)
    {
        "explicitNullMember",
        "sharedRouteOperations",
    };

    private static readonly JsonNode Plans = JsonNode.Parse(File.ReadAllText(
        Path.Combine(WireCorpus.FindRepoRoot(), "vendor", "spec", "cache", "v1", "plans.json")))!;

    private static IEnumerable<JsonNode> AllPlans => Plans["plans"]!.AsArray().Select(plan => plan!);

    private static bool Applies(JsonNode plan) =>
        !plan["capabilities"]!.AsArray().Any(capability => NotApplicable.Contains(capability!.GetValue<string>()));

    /// <summary>Every plan this SDK runs, by id.</summary>
    public static TheoryData<string> PlanIds()
    {
        var ids = new TheoryData<string>();
        foreach (var plan in AllPlans.Where(Applies)) ids.Add(plan["id"]!.GetValue<string>());
        return ids;
    }

    [Theory]
    [MemberData(nameof(PlanIds))]
    public async Task PlanHolds(string id)
    {
        var plan = AllPlans.Single(candidate => candidate["id"]!.GetValue<string>() == id);
        // A plan that refuses construction has no steps and is covered below.
        await using var runner = await CachePlanRunner.StartAsync(plan, WireCorpus.Vendored, Plans["denialCase"]!.GetValue<string>());
        if (runner is not null) await runner.RunAsync();
    }

    [Fact]
    public async Task RefusedConstructionPlansFailBeforeAnyRequest()
    {
        var refused = AllPlans.Where(plan => plan["client"]!["construction"]?.GetValue<string>() == "refused").ToList();
        Assert.NotEmpty(refused);
        foreach (var plan in refused)
        {
            Assert.Empty(plan["steps"]!.AsArray());
            Assert.Null(await CachePlanRunner.StartAsync(plan, WireCorpus.Vendored, Plans["denialCase"]!.GetValue<string>()));
        }
    }

    [Fact]
    public void OnlyDeclaredCapabilitiesAreNotApplicable()
    {
        var capabilities = Plans["capabilities"]!.AsObject();
        foreach (var name in NotApplicable)
            Assert.False(string.IsNullOrEmpty(capabilities[name]?["notApplicable"]?.GetValue<string>()),
                $"the plans do not allow {name} to be declared absent");
        // Cancellation and the scheme resource operations are implemented, not declared absent.
        Assert.DoesNotContain("cancellation", NotApplicable);
        Assert.DoesNotContain("schemeResourceOperations", NotApplicable);
    }

    [Fact]
    public void EveryPlannedCallHasADriver()
    {
        var planned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plan in AllPlans.Where(Applies))
            foreach (var step in plan["steps"]!.AsArray())
                if (step!["operation"] is JsonNode operation)
                    planned.Add($"{operation.GetValue<string>()}|{step["case"]!.GetValue<string>()}");
        var drivers = CachePlanCalls.Drivers.ToHashSet(StringComparer.Ordinal);
        Assert.True(planned.IsSubsetOf(drivers), "planned calls without a driver: " + string.Join(", ", planned.Except(drivers)));
        Assert.True(drivers.IsSubsetOf(planned), "drivers no plan uses: " + string.Join(", ", drivers.Except(planned)));
    }

    // An item goes unrun only when every plan for it needs a capability declared
    // absent above; C05 binds only a port with two operations over one route and body.
    [Fact]
    public void EveryChecklistItemIsCoveredByAPlanThisSdkRunsOrBindsOnlyAnAbsentCapability()
    {
        static IEnumerable<string> Covers(JsonNode plan) => plan["covers"]!.AsArray().Select(id => id!.GetValue<string>());
        var covered = AllPlans.Where(Applies).SelectMany(Covers).ToHashSet();
        var planned = AllPlans.SelectMany(Covers).ToHashSet();
        var missing = Plans["checklist"]!.AsArray().Select(item => item!["id"]!.GetValue<string>())
            .Where(id => !covered.Contains(id) && !planned.Contains(id));
        Assert.Empty(missing);
        Assert.Equal(new[] { "C05" }, planned.Except(covered).OrderBy(id => id, StringComparer.Ordinal));
    }

    // An unset optional member is always omitted, so the one plan that needs an
    // explicit null cannot be driven; this pins the reason it is declared absent.
    [Fact]
    public async Task UnsetOptionalMembersAreOmittedNotSentAsNull()
    {
        var bodies = new List<string>();
        var handler = new CapturingHandler(bodies);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://null.invalid/") };
        using var client = await EzClassifyClient.ConnectAsync(http, "ez-full", "1.0.0");
        await Assert.ThrowsAnyAsync<Exception>(() => client.Scheme!.ProcessAsync("OPEN", outputFormat: null, surface: null));
        Assert.Equal("""{"input":"OPEN"}""", bodies.Single());
    }

    // The mutation step proves independence only if it can change a shared result.
    [Fact]
    public void TheMutationStepChangesWhatIsMutable()
    {
        var shared = new Holder(new List<string> { "a" }, new[] { "b" }, new Dictionary<string, string> { ["k"] = "v" },
            new List<string> { "kept" }.AsReadOnly());
        Assert.Equal(3, CachePlanMutator.Mutate(shared));
        Assert.Empty(shared.List);
        Assert.Null(shared.Array[0]);
        Assert.Empty(shared.Map);
        Assert.Equal(new[] { "kept" }, shared.ReadOnly);
    }

    private sealed record Holder(
        IReadOnlyList<string> List, string?[] Array, IReadOnlyDictionary<string, string> Map, IReadOnlyList<string> ReadOnly);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly List<string> _bodies;
        public CapturingHandler(List<string> bodies) => _bodies = bodies;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            if (request.Content is not null) _bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(request.Content is null ? ContextProbe.Body : "{}", System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
