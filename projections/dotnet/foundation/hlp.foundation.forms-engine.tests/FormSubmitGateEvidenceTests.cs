using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Models;
using Harborline.Kernel.Core;
using Xunit;
using Harness = Harborline.Foundation.Forms.Engine.Tests.FormEngineOrchestrationTests.Harness;

namespace Harborline.Foundation.Forms.Engine.Tests;

/// <summary>
/// Decision D3, plan W5: the form submit-gate port returns the host decider's own evidence (C1,
/// <see cref="AccessProvider.CheckWithEvidenceAsync"/>), the engine binds it to the exact intended write and validates
/// every binding at runtime inside the existing submit path, and an ungated form_only submit is denied. A form without
/// a record type target keeps its verdict-only behaviour.
/// Oracles: every expected value is a literal (tenant, actor, form revision, instant, act spelling, payload fingerprint
/// computed outside the engine as SHA-256 of the canonical bytes <c>{"name":"Ada"}</c>), or the identity of the evidence
/// object the fixture decider itself produced. Nothing is read back from the production code under test.
/// </summary>
public sealed class FormSubmitGateEvidenceTests
{
    private const string TenantValue = "tenant-engine";
    private const string Actor = "alice";
    private const string FormRevision = "inspection@1.0.0";
    private const string Target = "notes.entry";
    private const string AdaFingerprint = "88bab6d8f6dc68a877064d584cbb5b6c50e74f617ea50d81d3a53c2ee6ffbc4f";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
    private static readonly SubmitGate InspectorGate = new(Role: RoleReference.Domain("inspector"));

    // ---- evidence returned on allow and on deny ----------------------------------------------------------------------

    [Fact(DisplayName = "D3: an allowed targeted submit returns the decider's own evidence bound to the exact intent, payload and instance")]
    public async Task AllowedSubmitReturnsBoundEvidence()
    {
        var decider = new FixtureDecider(allowed: true);
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(decider), recordTypePolicies: FormOnly());
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1"));

        var authorization = Assert.IsType<FormSubmitAuthorization>(receipt.Authorization);
        Assert.Same(Assert.Single(decider.Produced), authorization.Evidence);
        Assert.True(authorization.Evidence.Allowed);
        Assert.Equal(TenantValue, authorization.Intent.Tenant.Value);
        Assert.Equal(Actor, authorization.Intent.SubmitterActorId);
        Assert.Equal("inspection", authorization.Intent.FormId.Value);
        Assert.Equal(new SemanticVersion(1, 0, 0), authorization.Intent.FormVersion);
        Assert.Equal(harness.Definition.SchemaRef, authorization.Intent.SchemaRef);
        Assert.Equal(Target, authorization.Intent.RecordTypeId);
        Assert.Equal("op-1", authorization.Intent.OperationId);
        Assert.Equal(Now, authorization.Intent.DecidedAt);
        Assert.Equal(InspectorGate, authorization.Intent.Gate);
        Assert.Null(authorization.Intent.CaseReference);
        Assert.Equal(receipt.InstanceId, authorization.InstanceId);
        Assert.Equal(AdaFingerprint, authorization.PayloadFingerprint);
        var stored = Assert.Single(harness.State.Idempotency.Values);
        Assert.Equal(AdaFingerprint, stored.Submission.RequestFingerprint);
        Assert.Null(stored.Receipt.Authorization);
        // F4: the bound schema reference and target are kept on the stored rows for recovery.
        Assert.Equal(harness.Definition.SchemaRef, stored.Submission.SchemaRef);
        Assert.Equal(Target, stored.Submission.RecordTypeId);
        Assert.Equal(harness.Definition.SchemaRef, stored.Projection.SchemaRef);
        Assert.Equal(Target, stored.Projection.RecordTypeId);
        Assert.Equal(Target, Assert.Single(harness.Projection.Delivered).RecordTypeId);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());

        // The one request the decider saw: act, principal, tenant, form revision, instant and gate fields, all literal.
        var asked = Assert.Single(decider.Requests);
        Assert.Equal("forms:submit-gate", asked.Operation);
        Assert.Equal(Actor, asked.Principal);
        Assert.Equal(TenantValue, asked.Tenant);
        Assert.Equal(TenantValue, asked.Record.Tenant);
        Assert.Equal("form", asked.Record.Kind);
        Assert.Equal(FormRevision, asked.Record.Id);
        Assert.Equal(Now, asked.At);
        Assert.Equal("""{"role":{"vocabulary":"tax.roles","name":"inspector"},"standing":null,"capability":null}""",
            asked.Record.Fields["submit_gate"]!.ToJsonString());
        Assert.Equal(Target, asked.Record.Fields["record_type_id"]!.GetValue<string>());
    }

    [Fact(DisplayName = "D3: a denied targeted submit carries the decider's own denial evidence, resolves nothing and writes nothing")]
    public async Task DeniedSubmitReturnsDenialEvidence()
    {
        var decider = new FixtureDecider(allowed: false);
        var reuse = new SubstitutingReuse(definition => definition);
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(decider), reuseResolver: reuse, recordTypePolicies: FormOnly());
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var denied = await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1")));

        Assert.Equal("form.engine.denied", denied.Code);
        var denialEvidence = Assert.IsType<AuthorizationDecisionEvidence>(denied.Evidence);
        Assert.Same(Assert.Single(decider.Produced), denialEvidence);
        Assert.False(denialEvidence.Allowed);
        Assert.Equal("NoGrant", denialEvidence.Refusal);
        Assert.Equal(0, reuse.Calls);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    // ---- revocation ---------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "D3/D4: after revocation a new submit and a replay of an accepted one are both denied before anything is persisted or disclosed")]
    public async Task RevocationBeforeSubmitDeniesWithNothingPersisted()
    {
        var decider = new FixtureDecider(allowed: true);
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(decider), recordTypePolicies: FormOnly());
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());

        decider.Allowed = false;

        var fresh = await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-2")));
        var replay = await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1")));

        Assert.False(Assert.IsType<AuthorizationDecisionEvidence>(fresh.Evidence).Allowed);
        Assert.False(Assert.IsType<AuthorizationDecisionEvidence>(replay.Evidence).Allowed);
        Assert.Equal(3, decider.Produced.Count);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    // ---- forged or mismatched evidence --------------------------------------------------------------------------------

    public static TheoryData<string> ForgedEvidence =>
        ["tenant", "submitter", "form-id", "form-version", "instant", "operation", "record-kind"];

    [Theory(DisplayName = "D3: evidence the decider produced for any other tenant, submitter, form revision, instant or act is refused and withheld")]
    [MemberData(nameof(ForgedEvidence))]
    public async Task ForgedEvidenceIsRefused(string field)
    {
        var request = field switch
        {
            "tenant" => Request(tenant: "tenant-other"),
            "submitter" => Request(principal: "mallory"),
            "form-id" => Request(recordId: "other@1.0.0"),
            "form-version" => Request(recordId: "inspection@2.0.0"),
            "instant" => Request(at: Now.AddSeconds(1)),
            "operation" => Request(operation: "records:write"),
            _ => Request(recordKind: "record"),
        };
        var forged = Evidence(request, allowed: true);
        var port = new ScriptedPort(asked => new(asked.Intent, true, forged));
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: port, recordTypePolicies: FormOnly());
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var denied = await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1")));

        Assert.Null(denied.Evidence);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "D3: a literal request for exactly this intent is bound; the forged-evidence theory changes one field of it")]
    public async Task LiteralRequestEvidenceIsBound()
    {
        var genuine = Evidence(Request(), allowed: true);
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: new ScriptedPort(asked => new(asked.Intent, true, genuine)), recordTypePolicies: FormOnly());
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1"));

        Assert.Same(genuine, Assert.IsType<FormSubmitAuthorization>(receipt.Authorization).Evidence);
    }

    public static TheoryData<string> MismatchedIntent =>
        ["operation-id", "record-type", "schema-ref", "form-version", "form-id", "tenant", "submitter", "decided-at"];

    [Theory(DisplayName = "D3: an answer decided for another operation, target type, schema, form revision, tenant, submitter or instant is refused")]
    [MemberData(nameof(MismatchedIntent))]
    public async Task AnswerForAnotherIntentIsRefused(string field)
    {
        var decider = new FixtureDecider(allowed: true);
        var genuine = Port(decider);
        var port = new AsyncScriptedPort(async asked =>
        {
            var answer = await genuine.DecideAsync(asked);
            var other = field switch
            {
                "operation-id" => answer.Intent with { OperationId = "op-other" },
                "record-type" => answer.Intent with { RecordTypeId = "notes.other" },
                "schema-ref" => answer.Intent with { SchemaRef = new SchemaId("sha256:other") },
                "form-version" => answer.Intent with { FormVersion = new SemanticVersion(2, 0, 0) },
                "form-id" => answer.Intent with { FormId = new FormDefinitionId("other") },
                "tenant" => answer.Intent with { Tenant = new TenantId("tenant-other") },
                "submitter" => answer.Intent with { SubmitterActorId = "mallory" },
                _ => answer.Intent with { DecidedAt = Now.AddSeconds(-1) },
            };
            return answer with { Intent = other };
        });
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: port, recordTypePolicies: FormOnly());
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var denied = await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1")));

        Assert.Null(denied.Evidence);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Theory(DisplayName = "D3: a port verdict that disagrees with its own bound evidence is denied, with that evidence attached")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task VerdictDisagreeingWithEvidenceIsDenied(bool satisfied, bool evidenceAllowed)
    {
        var evidence = Evidence(Request(), evidenceAllowed);
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: new ScriptedPort(asked => new(asked.Intent, satisfied, evidence)), recordTypePolicies: FormOnly());
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var denied = await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1")));

        Assert.Same(evidence, denied.Evidence);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "D3: a targeted form is denied when the port answers satisfied with no evidence, including a verdict-only port")]
    public async Task SatisfiedWithoutEvidenceIsDeniedForTargetedForm()
    {
        var verdictOnly = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: new VerdictOnlyPort(true), recordTypePolicies: FormOnly());
        var noEvidence = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: new ScriptedPort(asked => new(asked.Intent, true, null)), recordTypePolicies: FormOnly());
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await verdictOnly.Engine.SubmitAsync(new(verdictOnly.Definition.Id, candidate, "op-1")));
        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await noEvidence.Engine.SubmitAsync(new(noEvidence.Definition.Id, candidate, "op-1")));

        Assert.Equal((0, 0, 0, 0), await verdictOnly.Store.CountsAsync());
        Assert.Equal((0, 0, 0, 0), await noEvidence.Store.CountsAsync());
    }

    // ---- ungated form_only, role and engine targets -------------------------------------------------------------------

    public static TheoryData<string> FormOnlySources => ["no-reader", "unknown-type", "declared-form-only"];

    [Theory(DisplayName = "D3/K6: an ungated targeted form is denied (form_only, unknown type, or no policy reader) before the gate port or reuse is asked, and writes nothing")]
    [MemberData(nameof(FormOnlySources))]
    public async Task UngatedFormOnlySubmitIsDenied(string source)
    {
        var reader = source switch
        {
            "no-reader" => null,
            "unknown-type" => new FixedReader(null),
            _ => new FixedReader(RecordTypeCreationGate.FormOnly),
        };
        var decider = new FixtureDecider(allowed: true);
        var reuse = new SubstitutingReuse(definition => definition);
        var harness = await Harness.CreateAsync(
            definitionFactory: UngatedTargeted, submitGates: Port(decider), reuseResolver: reuse, recordTypePolicies: reader);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var denied = await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1")));

        Assert.Null(denied.Evidence);
        Assert.Empty(decider.Requests);
        Assert.Equal(0, reuse.Calls);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "D1: an ungated form targeting a role-created type submits without an added creation gate and carries no gate evidence")]
    public async Task UngatedRoleTargetSubmits()
    {
        var reader = new FixedReader(RecordTypeCreationGate.Role);
        var harness = await Harness.CreateAsync(definitionFactory: UngatedTargeted, recordTypePolicies: reader);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1"));

        Assert.Null(receipt.Authorization);
        Assert.Equal([(TenantValue, Target)], reader.Asked);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "D3: a form targeting an engine-created type is denied even with an allowing gate, and the decider is never asked")]
    public async Task EngineTargetIsDenied()
    {
        var decider = new FixtureDecider(allowed: true);
        var harness = await Harness.CreateAsync(
            definitionFactory: Targeted, submitGates: Port(decider), recordTypePolicies: new FixedReader(RecordTypeCreationGate.Engine));
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1")));

        Assert.Empty(decider.Requests);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "D3: a policy reader that fails denies a targeted submit closed")]
    public async Task FailingPolicyReaderDenies()
    {
        var harness = await Harness.CreateAsync(
            definitionFactory: Targeted, submitGates: Port(new FixtureDecider(allowed: true)), recordTypePolicies: new ThrowingReader());
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1")));

        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    // ---- cross-operation and payload substitution ---------------------------------------------------------------------

    public static TheoryData<string> Substitutions => ["record-type", "schema-ref", "form-version", "form-id", "tenant", "gate"];

    [Theory(DisplayName = "D3: a resolved definition that substitutes the decided target type, schema, revision, tenant or gate is denied before evaluation")]
    [MemberData(nameof(Substitutions))]
    public async Task EffectiveDefinitionSubstitutionIsDenied(string field)
    {
        var reuse = new SubstitutingReuse(definition => field switch
        {
            "record-type" => definition with { RecordTypeId = "notes.other" },
            "schema-ref" => definition with { SchemaRef = new SchemaId("sha256:other") },
            "form-version" => definition with { Version = new SemanticVersion(2, 0, 0) },
            "form-id" => definition with { Id = new FormDefinitionId("other") },
            "tenant" => definition with { Tenant = new TenantId("tenant-other") },
            _ => definition with { SubmitGate = new SubmitGate(Role: RoleReference.Domain("anyone")) },
        });
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(new FixtureDecider(allowed: true)), reuseResolver: reuse, recordTypePolicies: FormOnly());
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1")));

        Assert.Equal(1, reuse.Calls);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "D3: one operation id cannot carry another payload, and its replay is bound to the original instance under fresh evidence")]
    public async Task PayloadSubstitutionUnderOneOperationConflicts()
    {
        var decider = new FixtureDecider(allowed: true);
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(decider), recordTypePolicies: FormOnly());
        using var ada = JsonDocument.Parse("""{"name":"Ada"}""");
        using var bob = JsonDocument.Parse("""{"name":"Bob"}""");

        var first = await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));
        await Assert.ThrowsAsync<FormEngineIdempotencyConflictException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, bob, "op-1")));
        var replay = await harness.Engine.SubmitAsync(new(harness.Definition.Id, ada, "op-1"));

        Assert.Equal(first.InstanceId, replay.InstanceId);
        var replayAuthorization = Assert.IsType<FormSubmitAuthorization>(replay.Authorization);
        var firstAuthorization = Assert.IsType<FormSubmitAuthorization>(first.Authorization);
        Assert.Equal(first.InstanceId, replayAuthorization.InstanceId);
        Assert.Equal(AdaFingerprint, replayAuthorization.PayloadFingerprint);
        Assert.Same(decider.Produced[2], replayAuthorization.Evidence);
        Assert.NotSame(firstAuthorization.Evidence, replayAuthorization.Evidence);
        Assert.Equal(AdaFingerprint, Assert.Single(harness.State.Idempotency.Values).Submission.RequestFingerprint);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "D3: a port replaying the first operation's decision for a second operation is refused")]
    public async Task DecisionReplayedForAnotherOperationIsRefused()
    {
        var decider = new FixtureDecider(allowed: true);
        var cached = new CachingPort(Port(decider));
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: cached, recordTypePolicies: FormOnly());
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");
        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1"));

        var denied = await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-2")));

        Assert.Null(denied.Evidence);
        Assert.Single(decider.Produced);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "D3: stale evidence from an earlier decision, re-wrapped for the new intent, is refused by its instant")]
    public async Task StaleEvidenceIsRefusedByItsInstant()
    {
        var decider = new FixtureDecider(allowed: true);
        var genuine = Port(decider);
        AuthorizationDecisionEvidence? firstEvidence = null;
        var port = new AsyncScriptedPort(async asked =>
        {
            if (firstEvidence is null)
            {
                var answer = await genuine.DecideAsync(asked);
                firstEvidence = answer.Evidence;
                return answer;
            }
            return new(asked.Intent, true, firstEvidence);
        });
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: port, clock: new SecondClock(Now), recordTypePolicies: FormOnly());
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");
        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1"));

        var denied = await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-2")));

        Assert.Null(denied.Evidence);
        Assert.Equal(Now, Assert.IsType<AuthorizationDecisionEvidence>(firstEvidence).At);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    // ---- idempotent replay binds the stored write (F1) ----------------------------------------------------------------

    [Fact(DisplayName = "D3 (F1): another submitter replaying an accepted operation id with the same payload conflicts and gets nothing of the original")]
    public async Task ReplayByAnotherSubmitterConflicts()
    {
        var state = new InMemoryFormSubmissionState();
        var alice = await Harness.CreateAsync(state: state, definitionFactory: Targeted,
            submitGates: Port(new FixtureDecider(allowed: true)), recordTypePolicies: FormOnly());
        var bobDecider = new FixtureDecider(allowed: true);
        var bob = await Harness.CreateAsync(state: state, definitionFactory: Targeted,
            submitGates: Port(bobDecider), recordTypePolicies: FormOnly(), actorId: "bob");
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");
        var original = await alice.Engine.SubmitAsync(new(alice.Definition.Id, candidate, "op-1"));

        await Assert.ThrowsAsync<FormEngineIdempotencyConflictException>(
            async () => await bob.Engine.SubmitAsync(new(bob.Definition.Id, candidate, "op-1")));

        Assert.Equal("bob", Assert.Single(bobDecider.Requests).Principal);
        var stored = Assert.Single(state.Idempotency.Values);
        Assert.Equal(Actor, stored.Submission.ActorId);
        Assert.Equal(original.InstanceId, stored.Submission.InstanceId);
        Assert.Equal((1, 1, 0, 1), await alice.Store.CountsAsync());
    }

    [Fact(DisplayName = "D3 (F1): replaying an operation accepted on version 1.0.0 against published 1.1.0 conflicts")]
    public async Task ReplayAcrossFormVersionsConflicts()
    {
        var state = new InMemoryFormSubmissionState();
        var first = await Harness.CreateAsync(state: state, definitionFactory: Targeted,
            submitGates: Port(new FixtureDecider(allowed: true)), recordTypePolicies: FormOnly());
        var later = await Harness.CreateAsync(state: state,
            definitionFactory: (schema, tenant) => Targeted(schema, tenant) with { Version = new SemanticVersion(1, 1, 0) },
            submitGates: Port(new FixtureDecider(allowed: true)), recordTypePolicies: FormOnly());
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");
        await first.Engine.SubmitAsync(new(first.Definition.Id, candidate, "op-1"));

        await Assert.ThrowsAsync<FormEngineIdempotencyConflictException>(
            async () => await later.Engine.SubmitAsync(new(later.Definition.Id, candidate, "op-1")));

        Assert.Equal(new SemanticVersion(1, 0, 0), Assert.Single(state.Idempotency.Values).Submission.DefinitionVersion);
        Assert.Equal((1, 1, 0, 1), await first.Store.CountsAsync());
    }

    [Fact(DisplayName = "D3 (F1, F8): replaying an operation under another case reference conflicts; the same case replays")]
    public async Task ReplayUnderAnotherCaseReferenceConflicts()
    {
        var harness = await Harness.CreateAsync(definitionFactory: Targeted,
            submitGates: Port(new FixtureDecider(allowed: true)), recordTypePolicies: FormOnly());
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");
        var first = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1", "case-1"));

        var same = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1", "case-1"));
        await Assert.ThrowsAsync<FormEngineIdempotencyConflictException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1", "case-2")));

        var sameAuthorization = Assert.IsType<FormSubmitAuthorization>(same.Authorization);
        Assert.Equal(first.InstanceId, sameAuthorization.InstanceId);
        Assert.Equal("case-1", sameAuthorization.Intent.CaseReference);
        Assert.Equal("case-1", Assert.Single(harness.State.Idempotency.Values).Projection.CaseReference);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    [Theory(DisplayName = "D4 (R2-7): a replay without evidence is refused for any caller but the original submitter, who still replays")]
    [InlineData("ungated")]
    [InlineData("verdict-only")]
    public async Task ReplayWithoutEvidenceIsBoundToTheSubmitter(string shape)
    {
        var state = new InMemoryFormSubmissionState();
        Func<string, TenantId, FormDefinition>? factory = shape == "ungated" ? null : Gated;
        IFormSubmitGateAccess? gates = shape == "ungated" ? null : new VerdictOnlyPort(true);
        var alice = await Harness.CreateAsync(state: state, definitionFactory: factory, submitGates: gates);
        var bob = await Harness.CreateAsync(state: state, definitionFactory: factory, submitGates: gates, actorId: "bob");
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");
        var original = await alice.Engine.SubmitAsync(new(alice.Definition.Id, candidate, "op-1"));

        await Assert.ThrowsAsync<FormEngineIdempotencyConflictException>(
            async () => await bob.Engine.SubmitAsync(new(bob.Definition.Id, candidate, "op-1")));
        var replay = await alice.Engine.SubmitAsync(new(alice.Definition.Id, candidate, "op-1"));

        Assert.Equal(original.InstanceId, replay.InstanceId);
        Assert.Null(replay.Authorization);
        Assert.Equal(Actor, Assert.Single(state.Idempotency.Values).Submission.ActorId);
        Assert.Equal((1, 1, 0, 1), await alice.Store.CountsAsync());
    }

    [Fact(DisplayName = "D4 (R3-2): an ungated replay is denied when the store does not return the stored write, and nothing is disclosed")]
    public async Task ReplayWithoutStoredWriteIsDenied()
    {
        var harness = await Harness.CreateAsync(storeDecorator: inner =>
            new RewritingStore(inner, result => result with { Stored = null }));
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");
        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1"));

        var denied = await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1")));

        Assert.Equal("form.engine.denied", denied.Code);
        Assert.Null(denied.Evidence);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "D4 (R3-2): a replay whose stored write names another tenant conflicts and discloses no receipt")]
    public async Task ReplayOfAnotherTenantsWriteConflicts()
    {
        var harness = await Harness.CreateAsync(storeDecorator: inner =>
            new RewritingStore(inner, result =>
            {
                var stored = result.Stored ?? throw new InvalidOperationException("the in-memory store returns the stored write");
                return result with { Stored = stored with { Submission = stored.Submission with { Tenant = new TenantId("tenant-other") } } };
            }));
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");
        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1"));

        await Assert.ThrowsAsync<FormEngineIdempotencyConflictException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1")));

        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    // ---- K6: no policy default inside Forms (F3) ---------------------------------------------------------------------

    [Fact(DisplayName = "K6 (F3): a gated targeted form with no policy reader is denied before the decider is asked")]
    public async Task TargetedFormWithoutReaderIsDenied()
    {
        var decider = new FixtureDecider(allowed: true);
        var harness = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(decider));
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1")));

        Assert.Empty(decider.Requests);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "K6 (F3): a gated form targeting a type the tenant does not have is denied")]
    public async Task UnknownTargetTypeIsDenied()
    {
        var harness = await Harness.CreateAsync(definitionFactory: Targeted,
            submitGates: Port(new FixtureDecider(allowed: true)), recordTypePolicies: new FixedReader(null));
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1")));

        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    // ---- reserved engine principals (F5) -----------------------------------------------------------------------------

    [Theory(DisplayName = "D3 (F5): an engine or reserved principal cannot submit any form; nothing is asked or written")]
    [InlineData("sys.scheduler")]
    [InlineData("system")]
    [InlineData("SYSTEM")]
    [InlineData("system:consent-expiry-sweep")]
    [InlineData("installer:platform-pack")]
    [InlineData("Sys.Dev-Indexer")]
    public async Task ReservedPrincipalCannotSubmit(string actor)
    {
        var decider = new FixtureDecider(allowed: true);
        var gated = await Harness.CreateAsync(definitionFactory: Gated, submitGates: Port(decider), actorId: actor);
        var ungated = await Harness.CreateAsync(actorId: actor);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var gatedDenial = await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await gated.Engine.SubmitAsync(new(gated.Definition.Id, candidate, "op-1")));
        var ungatedDenial = await Assert.ThrowsAsync<FormEngineDeniedException>(
            async () => await ungated.Engine.SubmitAsync(new(ungated.Definition.Id, candidate, "op-1")));

        Assert.Equal("form.engine.denied", gatedDenial.Code);
        Assert.Equal("form.engine.denied", ungatedDenial.Code);
        Assert.Empty(decider.Requests);
        Assert.Equal((0, 0, 0, 0), await gated.Store.CountsAsync());
        Assert.Equal((0, 0, 0, 0), await ungated.Store.CountsAsync());
    }

    [Theory(DisplayName = "D3 (F5): ordinary principals that only resemble a reserved namespace still submit")]
    [InlineData("sysadmin")]
    [InlineData("systems-team")]
    [InlineData("alice.system")]
    public async Task NonReservedLookalikeSubmits(string actor)
    {
        var harness = await Harness.CreateAsync(actorId: actor);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1"));

        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    // ---- targeted blank read (render implied by submit) ---------------------------------------------------------------

    [Fact(DisplayName = "D3 (F7): a blank read implied by submit permission on a targeted form needs bound evidence, not a bare verdict")]
    public async Task TargetedBlankReadNeedsBoundEvidence()
    {
        var submitOnly = new HashSet<FormEngineAction> { FormEngineAction.Submit };
        var verdictOnly = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: new VerdictOnlyPort(true),
            recordTypePolicies: FormOnly(), grantedActions: submitOnly);
        var decider = new FixtureDecider(allowed: true);
        var evidenced = await Harness.CreateAsync(definitionFactory: Targeted, submitGates: Port(decider),
            recordTypePolicies: FormOnly(), grantedActions: submitOnly);
        var ungated = await Harness.CreateAsync(definitionFactory: UngatedTargeted,
            recordTypePolicies: FormOnly(), grantedActions: submitOnly);

        await Assert.ThrowsAsync<FormEngineDeniedException>(async () => await verdictOnly.Engine.RenderAsync(verdictOnly.Definition.Id, null));
        var view = await evidenced.Engine.RenderAsync(evidenced.Definition.Id, null);
        await Assert.ThrowsAsync<FormEngineDeniedException>(async () => await ungated.Engine.RenderAsync(ungated.Definition.Id, null));

        Assert.Equal("inspection", view.FormId);
        var asked = Assert.Single(decider.Requests);
        Assert.Equal(FormRevision, asked.Record.Id);
    }

    // ---- staged-commit binding (runtime check inside the submit transaction) -----------------------------------------

    public static TheoryData<string> CommitMismatches =>
    [
        "commit-key", "operation-key", "command-id", "operation-fingerprint", "submission-instance", "submission-tenant",
        "submission-actor", "submission-form", "submission-version", "submission-fingerprint", "audit-instance", "audit-tenant",
        "audit-actor", "projection-instance", "projection-tenant", "projection-actor", "projection-form", "projection-version",
        "receipt-instance", "read-only-intent", "evidence-principal", "submission-schema", "submission-target",
        "projection-schema", "projection-target", "projection-case", "legacy-row-without-schema",
    ];

    [Theory(DisplayName = "D3 (F5): the reserved principal namespaces, by literal")]
    [InlineData("system", true)]
    [InlineData("sys.scheduler", true)]
    [InlineData("system:x", true)]
    [InlineData("installer:x", true)]
    [InlineData("sysadmin", false)]
    [InlineData("systemd", false)]
    [InlineData("alice", false)]
    public void ReservedPrincipalNamespaces(string actor, bool reserved) =>
        Assert.Equal(reserved, ReservedFormPrincipals.IsReserved(actor));

    [Fact(DisplayName = "D3: a staged commit that is exactly the decided write binds")]
    public void ExactCommitBinds()
    {
        var (authorization, commit, operation) = Staged();

        authorization.RequireBinds(commit, operation);
    }

    [Theory(DisplayName = "D3: a staged commit or operation differing from the decided write in any bound member is denied")]
    [MemberData(nameof(CommitMismatches))]
    public void MismatchedCommitIsDenied(string field)
    {
        var (authorization, commit, operation) = Staged();
        var other = new EntityId("harborline", "forms", "other-instance");
        var otherTenant = new TenantId("tenant-other");
        var otherForm = new FormDefinitionId("other");
        var otherVersion = new SemanticVersion(2, 0, 0);
        switch (field)
        {
            case "commit-key": commit = commit with { IdempotencyKey = "op-other" }; break;
            case "operation-key": operation = operation with { IdempotencyKey = "op-other" }; break;
            case "command-id": operation = operation with { CommandId = other.ToString() }; break;
            case "operation-fingerprint": operation = operation with { Fingerprint = "other" }; break;
            case "submission-instance": commit = commit with { Submission = commit.Submission with { InstanceId = other } }; break;
            case "submission-tenant": commit = commit with { Submission = commit.Submission with { Tenant = otherTenant } }; break;
            case "submission-actor": commit = commit with { Submission = commit.Submission with { ActorId = "mallory" } }; break;
            case "submission-form": commit = commit with { Submission = commit.Submission with { FormId = otherForm } }; break;
            case "submission-version": commit = commit with { Submission = commit.Submission with { DefinitionVersion = otherVersion } }; break;
            case "submission-fingerprint": commit = commit with { Submission = commit.Submission with { RequestFingerprint = "other" } }; break;
            case "audit-instance": commit = commit with { Audit = commit.Audit with { InstanceId = other } }; break;
            case "audit-tenant": commit = commit with { Audit = commit.Audit with { Tenant = otherTenant } }; break;
            case "audit-actor": commit = commit with { Audit = commit.Audit with { ActorId = "mallory" } }; break;
            case "projection-instance": commit = commit with { Projection = commit.Projection with { InstanceId = other } }; break;
            case "projection-tenant": commit = commit with { Projection = commit.Projection with { Tenant = otherTenant } }; break;
            case "projection-actor": commit = commit with { Projection = commit.Projection with { ActorId = "mallory" } }; break;
            case "projection-form": commit = commit with { Projection = commit.Projection with { FormId = otherForm } }; break;
            case "projection-version": commit = commit with { Projection = commit.Projection with { DefinitionVersion = otherVersion } }; break;
            case "receipt-instance": commit = commit with { Receipt = commit.Receipt with { InstanceId = other } }; break;
            case "read-only-intent": authorization = authorization with { Intent = authorization.Intent with { OperationId = null } }; break;
            case "submission-schema": commit = commit with { Submission = commit.Submission with { SchemaRef = new SchemaId("sha256:other") } }; break;
            case "submission-target": commit = commit with { Submission = commit.Submission with { RecordTypeId = "notes.other" } }; break;
            case "projection-schema": commit = commit with { Projection = commit.Projection with { SchemaRef = new SchemaId("sha256:other") } }; break;
            case "projection-target": commit = commit with { Projection = commit.Projection with { RecordTypeId = null } }; break;
            case "projection-case": commit = commit with { Projection = commit.Projection with { CaseReference = "case-other" } }; break;
            case "legacy-row-without-schema": commit = commit with { Submission = commit.Submission with { SchemaRef = null } }; break;
            default: authorization = authorization with { Evidence = Evidence(Request(principal: "mallory"), allowed: true) }; break;
        }

        Assert.Throws<FormEngineDeniedException>(() => authorization.RequireBinds(commit, operation));
    }

    // ---- forms without a target are unchanged ------------------------------------------------------------------------

    [Fact(DisplayName = "untargeted: an untargeted gated form with a verdict-only port submits as before, carries no authorization and never reads the policy")]
    public async Task UntargetedVerdictOnlyFormIsUnchanged()
    {
        var reader = new FixedReader(RecordTypeCreationGate.Engine);
        var harness = await Harness.CreateAsync(definitionFactory: Gated, submitGates: new VerdictOnlyPort(true), recordTypePolicies: reader);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1"));

        Assert.Null(receipt.Authorization);
        Assert.Empty(reader.Asked);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    [Fact(DisplayName = "untargeted: an untargeted ungated form reads the clock once inside the submit scope and carries no authorization")]
    public async Task UntargetedUngatedFormIsUnchanged()
    {
        var clock = new CountingClock(Now);
        var reader = new FixedReader(RecordTypeCreationGate.Engine);
        var harness = await Harness.CreateAsync(clock: clock, recordTypePolicies: reader);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1"));

        Assert.Null(receipt.Authorization);
        Assert.Equal(1, clock.Reads);
        Assert.Empty(reader.Asked);
        Assert.Equal(Now, receipt.SubmittedAt);
    }

    [Fact(DisplayName = "D3: an untargeted gated form with the decider port gets the evidence too, with no target in its intent")]
    public async Task UntargetedFormWithEvidencePortCarriesEvidence()
    {
        var decider = new FixtureDecider(allowed: true);
        var harness = await Harness.CreateAsync(definitionFactory: Gated, submitGates: Port(decider));
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "op-1"));

        var authorization = Assert.IsType<FormSubmitAuthorization>(receipt.Authorization);
        Assert.Same(Assert.Single(decider.Produced), authorization.Evidence);
        Assert.Null(authorization.Intent.RecordTypeId);
        Assert.Null(Assert.Single(decider.Requests).Record.Fields["record_type_id"]);
    }

    // ---- fixtures ------------------------------------------------------------------------------------------------------

    private static FormDefinition Gated(string schemaId, TenantId tenant) =>
        Harness.CreateDefinition(schemaId, tenant) with { SubmitGate = InspectorGate };

    private static FormDefinition Targeted(string schemaId, TenantId tenant) =>
        Harness.CreateDefinition(schemaId, tenant) with { SubmitGate = InspectorGate, RecordTypeId = Target };

    // A Published head with a target and no gate: a row persisted before publication required a gate.
    private static FormDefinition UngatedTargeted(string schemaId, TenantId tenant) =>
        Harness.CreateDefinition(schemaId, tenant) with { RecordTypeId = Target };

    private static FixedReader FormOnly() => new(RecordTypeCreationGate.FormOnly);

    private static AccessProviderFormSubmitGateAccess Port(FixtureDecider decider) =>
        new(new AccessProvider(decider), new FixedClock(Now));

    private static AccessRequest Request(
        string operation = "forms:submit-gate", string principal = Actor, string tenant = TenantValue,
        string recordKind = "form", string recordId = FormRevision, DateTimeOffset? at = null) =>
        new(operation, principal, tenant, new AccessRecord(tenant, recordKind, recordId, new Dictionary<string, JsonNode?>()), at ?? Now);

    private static AuthorizationDecisionEvidence Evidence(AccessRequest request, bool allowed) =>
        new(request, allowed, allowed ? "None" : "NoGrant", "grant:fixture@1", [], []);

    private static (FormSubmitAuthorization, FormSubmissionCommit, KernelOperationIdentity) Staged()
    {
        var tenant = new TenantId(TenantValue);
        var party = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var instance = new EntityId("harborline", "forms", "instance-1");
        var form = new FormDefinitionId("inspection");
        var version = new SemanticVersion(1, 0, 0);
        var bytes = "{}"u8.ToArray();
        var schema = new SchemaId("sha256:staged");
        var intent = new FormSubmitIntent(tenant, Actor, form, version, schema, Target, "op-1", Now, InspectorGate, "case-1");
        var authorization = new FormSubmitAuthorization(intent, instance, "fingerprint-1", Evidence(Request(), allowed: true));
        var commit = new FormSubmissionCommit(
            "op-1",
            new FormSubmissionRecord(instance, tenant, party, Actor, form, version, "fingerprint-1", bytes, Now, schema, Target),
            new FormMutationAuditEnvelope("audit-1", instance, tenant, Actor, bytes, Now),
            new FormProjectionEnvelope("outbox-1", instance, tenant, party, Actor, form, version, "case-1", bytes, Now,
                SchemaRef: schema, RecordTypeId: Target),
            new FormSubmitReceipt(instance, Now, FormProjectionStatus.Pending, []));
        return (authorization, commit, new KernelOperationIdentity(instance.ToString(), "op-1", "fingerprint-1"));
    }

    private sealed class FixtureDecider(bool allowed) : IAuthorizationDecider
    {
        public bool Allowed { get; set; } = allowed;
        public List<AccessRequest> Requests { get; } = [];
        public List<AuthorizationDecisionEvidence> Produced { get; } = [];

        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var evidence = Evidence(request, Allowed);
            Produced.Add(evidence);
            return ValueTask.FromResult(evidence);
        }
    }

    private sealed class ScriptedPort(Func<FormSubmitGateRequest, FormSubmitGateDecision> answer) : IFormSubmitGateAccess
    {
        public ValueTask<bool> SatisfiesAsync(FormExecutionScope scope, FormDefinition definition, SubmitGate gate, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("the engine asks DecideAsync");

        public ValueTask<FormSubmitGateDecision> DecideAsync(FormSubmitGateRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(answer(request));
    }

    private sealed class AsyncScriptedPort(Func<FormSubmitGateRequest, Task<FormSubmitGateDecision>> answer) : IFormSubmitGateAccess
    {
        public ValueTask<bool> SatisfiesAsync(FormExecutionScope scope, FormDefinition definition, SubmitGate gate, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("the engine asks DecideAsync");

        public async ValueTask<FormSubmitGateDecision> DecideAsync(FormSubmitGateRequest request, CancellationToken cancellationToken = default) =>
            await answer(request);
    }

    // Passes every call through, and rewrites only the result of a commit the inner store reported as Replayed.
    private sealed class RewritingStore(
        IFormSubmissionTransactionStore inner,
        Func<FormSubmissionCommitResult, FormSubmissionCommitResult> rewriteReplay) : IFormSubmissionTransactionStore
    {
        public async ValueTask<IFormSubmissionTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            new Scope(await inner.BeginTransactionAsync(cancellationToken), rewriteReplay);
        public ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default) => inner.CommitAsync(commit, cancellationToken);
        public ValueTask<FormSubmissionRecord?> GetAsync(TenantId tenant, EntityId instanceId, CancellationToken cancellationToken = default) => inner.GetAsync(tenant, instanceId, cancellationToken);
        public ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, CancellationToken cancellationToken = default) => inner.LeasePendingAsync(maximum, cancellationToken);
        public ValueTask ReleaseProjectionLeaseAsync(string outboxId, CancellationToken cancellationToken = default) => inner.ReleaseProjectionLeaseAsync(outboxId, cancellationToken);
        public ValueTask CompleteProjectionAsync(string outboxId, IReadOnlyList<FormProjectionSkip> skips, CancellationToken cancellationToken = default) => inner.CompleteProjectionAsync(outboxId, skips, cancellationToken);
        public ValueTask RetryProjectionAsync(string outboxId, string stableErrorCode, CancellationToken cancellationToken = default) => inner.RetryProjectionAsync(outboxId, stableErrorCode, cancellationToken);

        private sealed class Scope(
            IFormSubmissionTransactionScope inner,
            Func<FormSubmissionCommitResult, FormSubmissionCommitResult> rewriteReplay) : IFormSubmissionTransactionScope
        {
            public async ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default)
            {
                var result = await inner.CommitAsync(commit, cancellationToken);
                return result.Disposition == FormSubmissionCommitDisposition.Replayed ? rewriteReplay(result) : result;
            }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    private sealed class CachingPort(IFormSubmitGateAccess inner) : IFormSubmitGateAccess
    {
        private FormSubmitGateDecision? _first;

        public ValueTask<bool> SatisfiesAsync(FormExecutionScope scope, FormDefinition definition, SubmitGate gate, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("the engine asks DecideAsync");

        public async ValueTask<FormSubmitGateDecision> DecideAsync(FormSubmitGateRequest request, CancellationToken cancellationToken = default) =>
            _first ??= await inner.DecideAsync(request, cancellationToken);
    }

    // Implements only the original bool member, so the engine reaches it through the default DecideAsync.
    private sealed class VerdictOnlyPort(bool satisfied) : IFormSubmitGateAccess
    {
        public ValueTask<bool> SatisfiesAsync(FormExecutionScope scope, FormDefinition definition, SubmitGate gate, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(satisfied);
    }

    private sealed class FixedReader(RecordTypeCreationGate? declared) : IFormRecordTypePolicyReader
    {
        public List<(string Tenant, string RecordType)> Asked { get; } = [];

        public ValueTask<RecordTypeCreationGate?> GetCreationGateAsync(TenantId tenant, string recordTypeId, CancellationToken cancellationToken = default)
        {
            Asked.Add((tenant.Value, recordTypeId));
            return ValueTask.FromResult(declared);
        }
    }

    private sealed class ThrowingReader : IFormRecordTypePolicyReader
    {
        public ValueTask<RecordTypeCreationGate?> GetCreationGateAsync(TenantId tenant, string recordTypeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<RecordTypeCreationGate?>(new IOException("records catalogue unavailable"));
    }

    private sealed class SubstitutingReuse(Func<FormDefinition, FormDefinition> effective) : IReuseResolver
    {
        public int Calls { get; private set; }

        public ValueTask<ResolvedFormDefinition> ResolveAsync(FormDefinition definition, CancellationToken ct = default)
        {
            Calls++;
            return ValueTask.FromResult(new ResolvedFormDefinition(effective(definition), new Dictionary<string, ReuseProvenance>()));
        }
    }

    private sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

    private sealed class CountingClock(DateTimeOffset instant) : TimeProvider
    {
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return instant;
        }
    }

    // Each read is one second after the previous one, starting at the given instant.
    private sealed class SecondClock(DateTimeOffset start) : TimeProvider
    {
        private int _reads;

        public override DateTimeOffset GetUtcNow() => start.AddSeconds(_reads++);
    }
}
