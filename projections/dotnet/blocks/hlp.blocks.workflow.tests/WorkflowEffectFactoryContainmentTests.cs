using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// ADR 0143 SC2 — the PERMANENT red-team fence for finding F-1 (2026-07-02 enforcement-seam review). The
/// reviewer PROVED the old no-side-door fence for the effect-factory path was evadable: a scratch class that
/// injected the (then-public) <c>IWorkflowEffectFactoryRegistry</c>, called <c>Resolve(cap)</c>, and invoked
/// <c>.Build(...)</c> via a renamed local obtained a built effect with NO CP/SoD/classification gate — and
/// PASSED both engine-core arch tests (the scans were identifier-coupled and scoped to <c>blocks-workflow/src</c>,
/// while PR (b)'s interpreter lives in <c>apps/</c>).
///
/// <para>
/// The fix is TYPE-LEVEL containment, not better regexes: the resolve+build surface
/// (<c>IWorkflowEffectFactory</c> + its <c>Build</c>, <c>IWorkflowEffectFactoryResolver.Resolve</c>, the
/// registry, the broker) is <see langword="internal"/> to <c>blocks-workflow</c>; only the PUBLIC
/// <see cref="IWorkflowEffectCatalog"/> (metadata: <see cref="IWorkflowEffectCatalog.IsEffectingCapability"/>
/// / <see cref="IWorkflowEffectCatalog.EffectKeyFor"/>) is visible to an untrusted assembly. A concrete host
/// factory implements the internal <c>IWorkflowEffectFactory</c> ONLY from an assembly this package trusts via
/// <c>[InternalsVisibleTo]</c>. So an <c>apps/</c> interpreter cannot even NAME the resolve/build types to
/// attempt the bypass — it will not compile.
/// </para>
///
/// <para>
/// This test class is that fence made permanent. The reflection assertions go RED if the resolve+build
/// surface is ever re-publicized (reopening the hole the reviewer proved open); the "trusted path" fact
/// reproduces the reviewer's EXACT <c>Resolve().Build()</c> shape from inside this <c>[InternalsVisibleTo]</c>'d
/// assembly to show the mechanics are unchanged — the ONLY thing that changed is WHO can reach them.
/// </para>
/// </summary>
public sealed class WorkflowEffectFactoryContainmentTests
{
    private static readonly Assembly EngineAssembly = typeof(IWorkflowEffectCatalog).Assembly;

    // ── The resolve+build surface is INTERNAL (an untrusted assembly cannot name it) ──────────────

    [Theory]
    [InlineData("Harborline.Blocks.Workflow.Durable.IWorkflowEffectFactory")]
    [InlineData("Harborline.Blocks.Workflow.Durable.IWorkflowEffectFactoryResolver")]
    [InlineData("Harborline.Blocks.Workflow.Durable.WorkflowEffectFactoryRegistry")]
    [InlineData("Harborline.Blocks.Workflow.Durable.WorkflowEffectBroker")]
    public void Resolve_and_build_surface_is_not_publicly_visible(string typeName)
    {
        var type = EngineAssembly.GetType(typeName, throwOnError: true)!;

        // A top-level type that is `internal` is NOT public. If a future change re-publicizes any of these
        // (e.g. makes the factory interface public again, as it was when the reviewer's bypass compiled),
        // this fails LOUDLY and points back at F-1.
        Assert.False(
            type.IsVisible,
            $"ADR 0143 SC2 / F-1 REGRESSION: '{typeName}' is publicly visible. The effect-factory " +
            "resolve+build surface MUST stay internal — a public Resolve()/Build() is exactly the path the " +
            "2026-07-02 reviewer used to bypass the broker-PEP. Keep it internal; expose only " +
            "IWorkflowEffectCatalog (metadata) + IWorkflowEffectBroker (gated effect) publicly.");
    }

    [Fact]
    public void The_public_catalog_exposes_no_path_to_a_factory_or_its_build()
    {
        // The public metadata seam must not leak the factory type or a Resolve. IsEffectingCapability +
        // EffectKeyFor only — bool + EffectKey?, no IWorkflowEffectFactory anywhere in the surface.
        var members = typeof(IWorkflowEffectCatalog).GetMembers(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(members, m => m.Name.Contains("Resolve", StringComparison.Ordinal));
        Assert.All(
            typeof(IWorkflowEffectCatalog).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            m =>
            {
                Assert.NotEqual("IWorkflowEffectFactory", m.ReturnType.Name);
                Assert.DoesNotContain(m.GetParameters(), p => p.ParameterType.Name == "IWorkflowEffectFactory");
            });
    }

    [Fact]
    public void No_public_type_in_the_engine_exposes_the_effect_factory_type()
    {
        // The reviewer's bypass required a PUBLIC path to a live IWorkflowEffectFactory. Prove none exists:
        // no exported (public) type has a public method/property/ctor whose signature names the factory type.
        // Since the factory interface is internal, GetExportedTypes() cannot include it and no exported member
        // can reference it — so this passes today and turns RED the instant someone re-publicizes the surface.
        const string factoryTypeName = "IWorkflowEffectFactory";
        var leaks = new List<string>();

        foreach (var type in EngineAssembly.GetExportedTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.ReturnType.Name == factoryTypeName ||
                    method.GetParameters().Any(p => p.ParameterType.Name == factoryTypeName))
                {
                    leaks.Add($"{type.FullName}.{method.Name}");
                }
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (prop.PropertyType.Name == factoryTypeName)
                {
                    leaks.Add($"{type.FullName}.{prop.Name}");
                }
            }
        }

        Assert.True(
            leaks.Count == 0,
            "ADR 0143 SC2 / F-1 REGRESSION: a public type exposes IWorkflowEffectFactory, reopening a public " +
            "path to a live factory (the reviewer's bypass). Members:\n" + string.Join("\n", leaks));
    }

    // ── The trusted path (this IVT'd assembly stands in for a trusted effects adapter) still works ──

    [Fact]
    public void The_reviewers_bypass_shape_is_confined_to_the_trusted_boundary()
    {
        // Reproduce the reviewer's EXACT Resolve(cap).Build(req) shape — but note it compiles ONLY because
        // this test assembly is granted [InternalsVisibleTo] (a trusted boundary, like PR (b)'s effects
        // adapter). An untrusted apps/ assembly cannot name IWorkflowEffectFactoryResolver / IWorkflowEffectFactory
        // at all, so it cannot write these two lines (proven by the reflection facts above). The mechanics are
        // unchanged; WHO can reach them is the whole fix.
        var factory = new RecordingFactory("ledger.post-journal-entry", WorkflowEffectReach.Internal);
        IWorkflowEffectFactoryResolver resolver = new WorkflowEffectFactoryRegistry([factory]);

        var made = resolver.Resolve("ledger.post-journal-entry");
        Assert.NotNull(made);
        var effect = made!.Build(SampleRequest("ledger.post-journal-entry"));

        Assert.NotNull(effect);
        Assert.Equal(1, factory.BuildCount);
    }

    private sealed class RecordingFactory(string capabilityRef, WorkflowEffectReach reach) : IWorkflowEffectFactory
    {
        public string CapabilityRef { get; } = capabilityRef;
        public WorkflowEffectReach Reach { get; } = reach;
        public int BuildCount { get; private set; }

        public WorkflowEffect Build(WorkflowEffectRequest request)
        {
            BuildCount++;
            return new WorkflowEffect((_, _) => Task.CompletedTask);
        }
    }

    private static WorkflowEffectRequest SampleRequest(string capabilityRef) => WorkflowEffectRequest.For(
        capabilityRef,
        new WorkflowInstanceRecord
        {
            Id = "wf-1", TenantId = "t1", DefinitionKey = "vendor-onboarding",
            DefinitionVersion = "1.0.0", CurrentStep = "post",
        },
        new WorkflowStepKey("wf-1", 0, "post"));
}
