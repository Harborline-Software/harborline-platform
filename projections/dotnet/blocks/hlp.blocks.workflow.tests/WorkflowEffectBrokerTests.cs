using System;
using System.Threading;
using System.Threading.Tasks;

using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// The .NET broker-PEP (ADR 0143 D1) — the effect-execution boundary. Proves the fence: a CP effect is
/// producible ONLY through a human confirm that passes separation-of-duties (D-INV-5); an AP effect flows
/// autonomously; an OutboundExternal effect is forced-CP (R1-E data-exfil residual); an unregistered
/// capability is fail-closed refused; and every CP decision is recorded for the D-INV-7 override-rate metric.
/// </summary>
public sealed class WorkflowEffectBrokerTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Agent = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string PostJe = "ledger.post-journal-entry"; // CP (registry), Internal reach
    private const string SendEmail = "notify.email";            // AP (registry), Internal reach
    private const string CallWebhook = "notify.webhook";        // AP (registry) but OutboundExternal ⇒ forced CP
    private const string Unregistered = "ledger.mystery-op";    // no factory

    /// <summary>A recording fake effect factory — captures Build calls and yields a flag-setting effect.</summary>
    private sealed class FakeEffectFactory(string capabilityRef, WorkflowEffectReach reach) : IWorkflowEffectFactory
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

    private static (WorkflowEffectBroker broker, CountingWorkflowApprovalDecisionSink sink,
        FakeEffectFactory je, FakeEffectFactory email, FakeEffectFactory webhook) NewBroker()
    {
        var je = new FakeEffectFactory(PostJe, WorkflowEffectReach.Internal);
        var email = new FakeEffectFactory(SendEmail, WorkflowEffectReach.Internal);
        var webhook = new FakeEffectFactory(CallWebhook, WorkflowEffectReach.OutboundExternal);
        var registry = new WorkflowEffectFactoryRegistry([je, email, webhook]);
        var authority = CapabilityAuthorityRegistry.FromMap(new Dictionary<string, ActionClassification>(StringComparer.Ordinal)
        {
            [PostJe] = ActionClassification.CP,
            [SendEmail] = ActionClassification.AP,
            [CallWebhook] = ActionClassification.AP, // AP in the registry — the OutboundExternal reach forces CP
        });
        var sink = new CountingWorkflowApprovalDecisionSink();
        var broker = new WorkflowEffectBroker(registry, authority, sink, TimeProvider.System);
        return (broker, sink, je, email, webhook);
    }

    private static WorkflowEffectRequest Req(string capabilityRef) => WorkflowEffectRequest.For(
        capabilityRef,
        new WorkflowInstanceRecord
        {
            Id = "wf-1", TenantId = "t1", DefinitionKey = "vendor-onboarding",
            DefinitionVersion = "1.0.0", CurrentStep = "post",
        },
        new WorkflowStepKey("wf-1", 0, "post"));

    // ── Classification ────────────────────────────────────────────────────────

    [Fact]
    public void Classify_registry_CP_is_CP_and_registry_AP_is_AP()
    {
        var (broker, _, _, _, _) = NewBroker();
        Assert.Equal(ActionClassification.CP, broker.Classify(PostJe));
        Assert.Equal(ActionClassification.AP, broker.Classify(SendEmail));
    }

    [Fact]
    public void Classify_unknown_capability_is_CP_fail_closed()
    {
        var (broker, _, _, _, _) = NewBroker();
        Assert.Equal(ActionClassification.CP, broker.Classify(Unregistered));
        Assert.True(broker.RequiresConfirmation(Unregistered));
    }

    [Fact]
    public void Classify_outbound_external_forces_CP_even_when_registry_says_AP()
    {
        // The R1-E data-exfil residual: an AP-labelled outbound-external effect is treated as CP.
        var (broker, _, _, _, _) = NewBroker();
        Assert.Equal(ActionClassification.CP, broker.Classify(CallWebhook));
        Assert.True(broker.RequiresConfirmation(CallWebhook));
    }

    // ── AP autonomous path ──────────────────────────────────────────────────────

    [Fact]
    public void BuildAutonomousEffect_builds_an_AP_effect_without_a_human()
    {
        var (broker, sink, _, email, _) = NewBroker();
        var effect = broker.BuildAutonomousEffect(Req(SendEmail));
        Assert.NotNull(effect);
        Assert.Equal(1, email.BuildCount);
        Assert.Equal(0, sink.TotalCount); // AP effects are not human decisions — no override-rate record.
    }

    [Fact]
    public void BuildAutonomousEffect_refuses_a_CP_capability()
    {
        var (broker, _, je, _, _) = NewBroker();
        var ex = Assert.Throws<WorkflowEffectAuthorizationException>(() => broker.BuildAutonomousEffect(Req(PostJe)));
        Assert.Equal(WorkflowEffectBrokerCodes.CpRequiresConfirmation, ex.Code);
        Assert.Equal(0, je.BuildCount); // nothing built on the refused path.
    }

    [Fact]
    public void BuildAutonomousEffect_refuses_an_outbound_external_capability_forced_to_CP()
    {
        var (broker, _, _, _, webhook) = NewBroker();
        var ex = Assert.Throws<WorkflowEffectAuthorizationException>(() => broker.BuildAutonomousEffect(Req(CallWebhook)));
        Assert.Equal(WorkflowEffectBrokerCodes.CpRequiresConfirmation, ex.Code);
        Assert.Equal(0, webhook.BuildCount);
    }

    [Fact]
    public void BuildAutonomousEffect_refuses_an_unregistered_capability()
    {
        var (broker, _, _, _, _) = NewBroker();
        var ex = Assert.Throws<WorkflowEffectAuthorizationException>(() => broker.BuildAutonomousEffect(Req(Unregistered)));
        Assert.Equal(WorkflowEffectBrokerCodes.UnregisteredCapability, ex.Code);
    }

    // ── CP confirm path (separation of duties) ──────────────────────────────────

    [Fact]
    public void ConfirmAndBuildEffect_builds_a_CP_effect_on_a_valid_distinct_human_confirm()
    {
        var (broker, sink, je, _, _) = NewBroker();
        var effect = broker.ConfirmAndBuildEffect(
            Req(PostJe),
            new WorkflowProposerIdentity(Agent, IsHuman: false), // an agent proposed
            new WorkflowConfirmerIdentity(Alice, IsHuman: true)); // a distinct human confirms
        Assert.NotNull(effect);
        Assert.Equal(1, je.BuildCount);
        Assert.Equal(1, sink.ConfirmedCount);
        Assert.Equal(0, sink.OverriddenCount);
    }

    [Fact]
    public void ConfirmAndBuildEffect_allows_a_human_proposer_to_self_confirm()
    {
        // The single-user-desktop accountable-human compensating control (§4.4): a human self-confirm is OK.
        var (broker, sink, je, _, _) = NewBroker();
        var effect = broker.ConfirmAndBuildEffect(
            Req(PostJe),
            new WorkflowProposerIdentity(Alice, IsHuman: true),
            new WorkflowConfirmerIdentity(Alice, IsHuman: true));
        Assert.NotNull(effect);
        Assert.Equal(1, je.BuildCount);
        Assert.Equal(1, sink.ConfirmedCount);
    }

    [Fact]
    public void ConfirmAndBuildEffect_refuses_a_non_human_confirmer_and_builds_nothing()
    {
        var (broker, sink, je, _, _) = NewBroker();
        var ex = Assert.Throws<WorkflowSodViolationException>(() => broker.ConfirmAndBuildEffect(
            Req(PostJe),
            new WorkflowProposerIdentity(Alice, IsHuman: true),
            new WorkflowConfirmerIdentity(Agent, IsHuman: false)));
        Assert.Equal(WorkflowApprovalCodes.SeparationOfDutiesViolation, ex.Code);
        Assert.Equal(0, je.BuildCount);   // fail-closed: the effect is NEVER built on an SoD violation.
        Assert.Equal(0, sink.TotalCount); // and a refused confirm does NOT pollute the override-rate metric.
    }

    [Fact]
    public void ConfirmAndBuildEffect_refuses_an_agent_self_confirm()
    {
        var (broker, _, je, _, _) = NewBroker();
        var ex = Assert.Throws<WorkflowSodViolationException>(() => broker.ConfirmAndBuildEffect(
            Req(PostJe),
            new WorkflowProposerIdentity(Agent, IsHuman: false),
            new WorkflowConfirmerIdentity(Agent, IsHuman: false)));
        Assert.Equal(WorkflowApprovalCodes.SeparationOfDutiesViolation, ex.Code);
        Assert.Equal(0, je.BuildCount);
    }

    [Fact]
    public void ConfirmAndBuildEffect_gates_an_outbound_external_effect_through_the_human_confirm()
    {
        // CallWebhook is AP in the registry but OutboundExternal ⇒ forced CP ⇒ needs the confirm path.
        var (broker, sink, _, _, webhook) = NewBroker();
        var effect = broker.ConfirmAndBuildEffect(
            Req(CallWebhook),
            new WorkflowProposerIdentity(Agent, IsHuman: false),
            new WorkflowConfirmerIdentity(Alice, IsHuman: true));
        Assert.NotNull(effect);
        Assert.Equal(1, webhook.BuildCount);
        Assert.Equal(1, sink.ConfirmedCount);
    }

    [Fact]
    public void ConfirmAndBuildEffect_refuses_an_AP_capability_wiring_error()
    {
        var (broker, _, _, email, _) = NewBroker();
        var ex = Assert.Throws<WorkflowEffectAuthorizationException>(() => broker.ConfirmAndBuildEffect(
            Req(SendEmail),
            new WorkflowProposerIdentity(Alice, IsHuman: true),
            new WorkflowConfirmerIdentity(Bob, IsHuman: true)));
        Assert.Equal(WorkflowEffectBrokerCodes.ApNotConfirmable, ex.Code);
        Assert.Equal(0, email.BuildCount);
    }

    [Fact]
    public void ConfirmAndBuildEffect_refuses_an_unregistered_capability()
    {
        var (broker, _, _, _, _) = NewBroker();
        var ex = Assert.Throws<WorkflowEffectAuthorizationException>(() => broker.ConfirmAndBuildEffect(
            Req(Unregistered),
            new WorkflowProposerIdentity(Alice, IsHuman: true),
            new WorkflowConfirmerIdentity(Bob, IsHuman: true)));
        Assert.Equal(WorkflowEffectBrokerCodes.UnregisteredCapability, ex.Code);
    }

    // ── D-INV-7 override-rate metric ────────────────────────────────────────────

    [Fact]
    public void RecordOverride_feeds_the_override_rate_metric()
    {
        var (broker, sink, _, _, _) = NewBroker();
        Assert.Null(sink.OverrideRate); // undefined before any decision.

        // one confirm, one override ⇒ 50% override rate.
        broker.ConfirmAndBuildEffect(Req(PostJe),
            new WorkflowProposerIdentity(Agent, IsHuman: false),
            new WorkflowConfirmerIdentity(Alice, IsHuman: true));
        broker.RecordOverride(Req(PostJe), new WorkflowConfirmerIdentity(Bob, IsHuman: true));

        Assert.Equal(1, sink.ConfirmedCount);
        Assert.Equal(1, sink.OverriddenCount);
        Assert.Equal(0.5, sink.OverrideRate);
    }

    // ── Registry duplicate guard ────────────────────────────────────────────────

    [Fact]
    public void EffectFactoryRegistry_refuses_two_factories_for_one_capability()
    {
        var dup1 = new FakeEffectFactory(PostJe, WorkflowEffectReach.Internal);
        var dup2 = new FakeEffectFactory(PostJe, WorkflowEffectReach.Internal);
        Assert.Throws<InvalidOperationException>(() => new WorkflowEffectFactoryRegistry([dup1, dup2]));
    }

    [Fact]
    public void EffectFactoryRegistry_exposes_the_effect_key_with_reach()
    {
        var (_, _, _, _, _) = NewBroker();
        var registry = new WorkflowEffectFactoryRegistry(
            [new FakeEffectFactory(CallWebhook, WorkflowEffectReach.OutboundExternal)]);
        var key = registry.EffectKeyFor(CallWebhook);
        Assert.NotNull(key);
        Assert.Equal(WorkflowEffectReach.OutboundExternal, key!.Value.Reach);
        Assert.True(registry.IsEffectingCapability(CallWebhook));
        Assert.Null(registry.EffectKeyFor(Unregistered));
    }
}
