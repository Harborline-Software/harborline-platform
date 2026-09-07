using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Registry;


using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>
/// The ADR 0146 D5 named-rule registry + version policies (Wave 1 unification floor). Pins:
/// resolve <c>latest</c>/<c>pinned</c>/<c>draft</c>; the board-F6 draft-exclusion MECHANISM
/// (production never returns a draft; sandbox reaches it); the S-8 monotonic-watermark offline
/// determinism (order-independent "latest"); D7 instance-pin deterministic replay; and the
/// HomeEpochFence read-tip-reject-stale discipline on the per-node compile/cache swap.
/// </summary>
public sealed class RuleRegistryTests
{
    private const string Tenant = "t1";
    private const string Key = "invoice.approval-threshold";

    private static RuleDefinition Def(string id) => RuleDefinitionFactory.Create(id, RuleTier.JsonLogic, RuleScope.Field, "f", "{\"+\":[1,2]}", RuleActionKind.Compute);

    private static PublishedRuleVersion Pub(string version, bool draft = false)
        => new(Key, version, draft, Def($"{Key}@{version}"));

    // ── version comparator (S-8 watermark) ────────────────────────────────────

    [Fact]
    public void RuleVersion_orders_numeric_segments_not_lexically()
    {
        Assert.True(RuleVersion.Compare("1.10.0", "1.2.0") > 0);   // 10 > 2 numerically, not "10" < "2"
        Assert.True(RuleVersion.Compare("2.0.0", "1.9.9") > 0);
        Assert.Equal(0, RuleVersion.Compare("1.0.0", "1.0.0"));
        Assert.True(RuleVersion.Compare("1.0.0-rc1", "1.0.0") < 0); // pre-release precedes stable
        Assert.True(RuleVersion.IsDowngrade("1.2.0", "1.1.0"));      // candidate below watermark
        Assert.False(RuleVersion.IsDowngrade("1.2.0", "1.3.0"));
    }

    // ── latest ────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_latest_picks_the_highest_published_version()
    {
        var reg = new RuleRegistry();
        reg.Publish(Tenant, Pub("1.0.0"));
        reg.Publish(Tenant, Pub("1.10.0"));
        reg.Publish(Tenant, Pub("1.2.0"));

        var r = reg.Resolve(Tenant, Key, RuleVersionPolicy.Latest, RuleResolveScope.Production);

        Assert.True(r.IsResolved);
        Assert.Equal("1.10.0", r.Version);
        Assert.NotNull(r.Definition);
    }

    [Fact]
    public void Resolve_latest_is_order_independent_across_offline_peers()
    {
        // Two peers receive the same published set in OPPOSITE sync orders — both converge on the
        // same "latest" (S-8 monotonic determinism; board's "monotonically convergent" property).
        var peerA = new RuleRegistry();
        peerA.Publish(Tenant, Pub("1.0.0"));
        peerA.Publish(Tenant, Pub("2.0.0"));

        var peerB = new RuleRegistry();
        peerB.Publish(Tenant, Pub("2.0.0"));
        peerB.Publish(Tenant, Pub("1.0.0"));

        var a = peerA.Resolve(Tenant, Key, RuleVersionPolicy.Latest, RuleResolveScope.Production);
        var b = peerB.Resolve(Tenant, Key, RuleVersionPolicy.Latest, RuleResolveScope.Production);

        Assert.Equal("2.0.0", a.Version);
        Assert.Equal(a.Version, b.Version);
    }

    // ── draft exclusion (board F6 — a mechanism, not prose) ─────────────────────

    [Fact]
    public void Resolve_latest_excludes_drafts_on_production_but_reaches_them_in_sandbox()
    {
        var reg = new RuleRegistry();
        reg.Publish(Tenant, Pub("1.0.0"));
        reg.Publish(Tenant, Pub("2.0.0", draft: true)); // a higher-versioned DRAFT

        var prod = reg.Resolve(Tenant, Key, RuleVersionPolicy.Latest, RuleResolveScope.Production);
        var sandbox = reg.Resolve(Tenant, Key, RuleVersionPolicy.Latest, RuleResolveScope.Sandbox);

        Assert.Equal("1.0.0", prod.Version);   // production NEVER returns the draft
        Assert.Equal("2.0.0", sandbox.Version); // sandbox sees the draft
    }

    [Fact]
    public void Resolve_draft_policy_is_refused_on_production_and_resolves_in_sandbox()
    {
        var reg = new RuleRegistry();
        reg.Publish(Tenant, Pub("1.0.0"));
        reg.Publish(Tenant, Pub("2.0.0", draft: true));

        var prod = reg.Resolve(Tenant, Key, RuleVersionPolicy.Draft, RuleResolveScope.Production);
        var sandbox = reg.Resolve(Tenant, Key, RuleVersionPolicy.Draft, RuleResolveScope.Sandbox);

        Assert.Equal(RuleResolutionStatus.DraftRefused, prod.Status);
        Assert.False(prod.IsResolved);
        Assert.True(sandbox.IsResolved);
        Assert.Equal("2.0.0", sandbox.Version);
    }

    [Fact]
    public void Resolve_pinned_draft_is_refused_on_production_and_reachable_in_sandbox()
    {
        var reg = new RuleRegistry();
        reg.Publish(Tenant, Pub("3.0.0", draft: true));

        var prod = reg.Resolve(Tenant, Key, RuleVersionPolicy.Pinned("3.0.0"), RuleResolveScope.Production);
        var sandbox = reg.Resolve(Tenant, Key, RuleVersionPolicy.Pinned("3.0.0"), RuleResolveScope.Sandbox);

        Assert.Equal(RuleResolutionStatus.DraftRefused, prod.Status);
        Assert.Equal("3.0.0", prod.Version); // the refused draft version is reported
        Assert.True(sandbox.IsResolved);
        Assert.Equal("3.0.0", sandbox.Version);
    }

    // ── pinned + not-found ──────────────────────────────────────────────────────

    [Fact]
    public void Resolve_pinned_returns_the_exact_version()
    {
        var reg = new RuleRegistry();
        reg.Publish(Tenant, Pub("1.0.0"));
        reg.Publish(Tenant, Pub("2.0.0"));

        var r = reg.Resolve(Tenant, Key, RuleVersionPolicy.Pinned("1.0.0"), RuleResolveScope.Production);

        Assert.True(r.IsResolved);
        Assert.Equal("1.0.0", r.Version); // NOT the latest 2.0.0
    }

    [Fact]
    public void Resolve_returns_not_found_for_unknown_key_or_pin()
    {
        var reg = new RuleRegistry();
        reg.Publish(Tenant, Pub("1.0.0"));

        Assert.Equal(RuleResolutionStatus.NotFound,
            reg.Resolve(Tenant, "no.such.rule", RuleVersionPolicy.Latest, RuleResolveScope.Production).Status);
        Assert.Equal(RuleResolutionStatus.NotFound,
            reg.Resolve(Tenant, Key, RuleVersionPolicy.Pinned("9.9.9"), RuleResolveScope.Production).Status);
        Assert.Equal(RuleResolutionStatus.NotFound,
            reg.Resolve("other-tenant", Key, RuleVersionPolicy.Latest, RuleResolveScope.Production).Status);
    }

    // ── D7 instance pin ─────────────────────────────────────────────────────────

    [Fact]
    public void Pin_freezes_the_version_and_replays_deterministically_past_a_later_publish()
    {
        var reg = new RuleRegistry();
        reg.Publish(Tenant, Pub("1.0.0"));

        var pin = reg.Pin(Tenant, Key, RuleVersionPolicy.Latest, RuleResolveScope.Production);
        Assert.NotNull(pin);
        Assert.Equal("1.0.0", pin!.Version);

        // A newer version publishes AFTER the consumer instance pinned.
        reg.Publish(Tenant, Pub("2.0.0"));

        // Latest now moves; the pin does NOT (deterministic replay against the creation-time version).
        Assert.Equal("2.0.0", reg.Resolve(Tenant, Key, RuleVersionPolicy.Latest, RuleResolveScope.Production).Version);
        Assert.Equal("1.0.0", reg.ResolvePinned(pin, RuleResolveScope.Production).Version);
    }

    [Fact]
    public void Pin_returns_null_when_nothing_resolves()
    {
        var reg = new RuleRegistry();
        Assert.Null(reg.Pin(Tenant, Key, RuleVersionPolicy.Latest, RuleResolveScope.Production));
    }

    // ── compile/cache swap fence (HomeEpochFence discipline) ─────────────────────

    [Fact]
    public void Compile_cache_swap_is_monotonic_and_refuses_a_stale_publish()
    {
        var cache = new RuleCompileCache();
        var g10 = RuleCompiler.Compile(new[] { Def("r@1.0.0") });
        var g11 = RuleCompiler.Compile(new[] { Def("r@1.1.0") });
        var g10Late = RuleCompiler.Compile(new[] { Def("r@1.0.0-late") });

        cache.Swap(Tenant, Key, "1.0.0", g10);
        cache.Swap(Tenant, Key, "1.1.0", g11); // newer installs

        // A stale publish (a lower version arriving late) is REFUSED — the newer AST stands.
        var ex = Assert.Throws<StaleRulePublishException>(() => cache.Swap(Tenant, Key, "1.0.0", g10Late));
        Assert.Equal("1.0.0", ex.StaleVersion);
        Assert.Equal("1.1.0", ex.CurrentVersion);

        Assert.True(cache.TryGet(Tenant, Key, out var cachedVersion, out var cachedGraph));
        Assert.Equal("1.1.0", cachedVersion);        // the newer AST was not overwritten
        Assert.Same(g11, cachedGraph);
    }

    [Fact]
    public void Compile_cache_swap_is_idempotent_for_an_equal_version()
    {
        var cache = new RuleCompileCache();
        var first = RuleCompiler.Compile(new[] { Def("r@2.0.0") });
        var recompiled = RuleCompiler.Compile(new[] { Def("r@2.0.0") });

        cache.Swap(Tenant, Key, "2.0.0", first);
        cache.Swap(Tenant, Key, "2.0.0", recompiled); // equal version re-installs (no throw)

        Assert.True(cache.TryGet(Tenant, Key, out var version, out var graph));
        Assert.Equal("2.0.0", version);
        Assert.Same(recompiled, graph);
    }
}
