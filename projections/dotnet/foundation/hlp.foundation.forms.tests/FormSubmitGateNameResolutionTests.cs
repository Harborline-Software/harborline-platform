using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// T-485/submit-gate-name-resolution: the publish-time half of DES-0016 forms-auth-11. A submit gate's role arm resolves
/// through Access's role vocabulary and its standing arm through the standings installed standing rules declare (L356).
/// Publication refuses an arm whose register is absent, and a supplied register is honoured at every stage, which is
/// Layout's rule for the same shared gate (layout-auth-23). Oracle: the codes and pointers are the slice record's
/// literals (<c>/submit_gate/role</c>, <c>/submit_gate/standing/name</c>), not this code's output.
/// </summary>
public sealed class FormSubmitGateNameResolutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = new("tenant:acme");
    private static readonly SubmitGate UnknownRole = new(Role: RoleReference.Domain("approver"));
    private static readonly SubmitGate UnknownStanding = new(Standing: new RecordStandingReference("assigned-reviewer"));
    private static readonly SubmitGate KnownRole = new(Role: FormGateRegisters.Inspector);
    private static readonly SubmitGate KnownStanding = new(Standing: FormGateRegisters.Author);

    public static TheoryData<string, string> UnknownNamesOnEveryPath => new()
    {
        { "role", "atomic" }, { "role", "direct" }, { "role", "transition" }, { "role", "migration" },
        { "standing", "atomic" }, { "standing", "direct" }, { "standing", "transition" }, { "standing", "migration" },
    };

    [Theory(DisplayName = "T-485 gate names: a role or standing no register declares is refused by name on every path to Published, and nothing is published")]
    [MemberData(nameof(UnknownNamesOnEveryPath))]
    public async Task AnUndeclaredNameIsRefusedOnEveryPublicationPath(string arm, string path)
    {
        using var store = FormGateRegisters.Store(new FixedClock(Now));
        var gate = arm == "role" ? UnknownRole : UnknownStanding;
        var definition = Form(gate, $"names-{arm}-{path}");

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(async () => await PublishAsync(store, definition, path));

        Assert.Equal(Expected(arm), (refusal.Code, refusal.Target));
        // Nothing gated was published: either nothing is Published, or (migration) only the legacy ungated revision is.
        var current = await store.GetCurrentPublishedAsync(Tenant, definition.Id);
        Assert.True(current is null || current.SubmitGate is null);
    }

    [Theory(DisplayName = "T-485 gate names: a role and a standing the registers declare publish on every path")]
    [InlineData("atomic")]
    [InlineData("direct")]
    [InlineData("transition")]
    [InlineData("migration")]
    public async Task DeclaredNamesPublishOnEveryPath(string path)
    {
        using var store = FormGateRegisters.Store(new FixedClock(Now));

        foreach (var (gate, id) in new[] { (KnownRole, $"known-role-{path}"), (KnownStanding, $"known-standing-{path}") })
        {
            await PublishAsync(store, Form(gate, id), path);
            var published = await store.GetCurrentPublishedAsync(Tenant, new FormDefinitionId(id));
            Assert.Equal(gate, published?.SubmitGate);
        }
    }

    [Theory(DisplayName = "T-485 gate names: with no role vocabulary or no standing set, publication refuses that arm, as the capability arm is refused with no register")]
    [InlineData("role")]
    [InlineData("standing")]
    public async Task PublicationRefusesAnArmWhoseRegisterIsAbsent(string arm)
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var gate = arm == "role" ? KnownRole : KnownStanding;

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(Form(gate, $"no-register-{arm}")));

        Assert.Equal(Expected(arm), (refusal.Code, refusal.Target));
    }

    [Theory(DisplayName = "T-485 gate names: a Draft is not refused by an absent register, and is refused by a supplied one that does not declare the name")]
    [InlineData("role")]
    [InlineData("standing")]
    public async Task ADraftHonoursASuppliedRegisterOnly(string arm)
    {
        using var unregistered = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var undeclared = arm == "role" ? UnknownRole : UnknownStanding;
        var saved = await unregistered.RegisterAsync(Form(undeclared, $"draft-{arm}"));
        Assert.Equal((FormDefinitionStatus.Draft, undeclared), (saved.Status, saved.SubmitGate));

        using var registered = FormGateRegisters.Store(new FixedClock(Now));
        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await registered.RegisterAsync(Form(undeclared, $"draft-{arm}")));
        Assert.Equal(Expected(arm), (refusal.Code, refusal.Target));
    }

    [Fact(DisplayName = "T-485 gate names: a role arm on a form that creates Records resolves the same way")]
    public async Task ARoleArmOnARecordCreatingFormResolvesTheSameWay()
    {
        using var store = FormGateRegisters.Store(new FixedClock(Now), recordTypePolicies: new FormOnlyReader());

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(Form(UnknownRole, "targeted-unknown", "notes.entry")));
        Assert.Equal(Expected("role"), (refusal.Code, refusal.Target));

        var published = await store.RegisterAndPublishAsync(Form(KnownRole, "targeted-known", "notes.entry"));
        Assert.Equal((FormDefinitionStatus.Published, "notes.entry"), (published.Status, published.RecordTypeId));
    }

    [Theory(DisplayName = "T-485 gate names: on a form that creates Records, the standing refusal still fires first, whatever the standing register says")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheStandingOnRecordCreationRefusalComesFirst(bool registers)
    {
        using var store = registers
            ? FormGateRegisters.Store(new FixedClock(Now), recordTypePolicies: new FormOnlyReader())
            : new InMemoryFormDefinitionStore(new FixedClock(Now), recordTypePolicies: new FormOnlyReader());

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(Form(UnknownStanding, $"targeted-standing-{registers}", "notes.entry")));

        Assert.Equal((FormDefinitionCodes.SubmitGateStandingOnRecordCreation, "/submit_gate/standing"), (refusal.Code, refusal.Target));
    }

    [Fact(DisplayName = "T-485 gate names: a role reference outside the role vocabularies is refused by the role code, not an argument fault")]
    public async Task AMalformedRoleReferenceIsRefusedByName()
    {
        using var store = FormGateRegisters.Store(new FixedClock(Now));

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAndPublishAsync(Form(new SubmitGate(Role: new RoleReference("tenant.invented", "inspector")), "invented-vocabulary")));

        Assert.Equal(Expected("role"), (refusal.Code, refusal.Target));
    }

    private static (string, string) Expected(string arm) => arm == "role"
        ? ("form.submit_gate.role_unknown", "/submit_gate/role")
        : ("form.submit_gate.standing_unknown", "/submit_gate/standing/name");

    private static async Task PublishAsync(InMemoryFormDefinitionStore store, FormDefinition definition, string path)
    {
        switch (path)
        {
            case "atomic":
                await store.RegisterAndPublishAsync(definition);
                break;
            case "direct":
                await store.RegisterAsync(definition with { Status = FormDefinitionStatus.Published });
                break;
            case "transition":
                await store.RegisterAsync(definition);
                await store.PublishAsync(definition.Tenant, definition.Id, definition.Version);
                break;
            default:
                // A legacy ungated Published revision the seed migration gates and republishes (T-756).
                store.SeedLegacyRevisionForTesting(definition with { Status = FormDefinitionStatus.Published, SubmitGate = null });
                await new SubmitGateSeedMigration(store).ApplyAsync(
                    definition.Tenant, new Dictionary<FormDefinitionId, SubmitGate> { [definition.Id] = definition.SubmitGate! });
                break;
        }
    }

    private static FormDefinition Form(SubmitGate gate, string id, string? target = null)
    {
        var fields = new Dictionary<string, FieldOverlay> { ["name"] = new(InternationalizedText.FromInvariant("name")) };
        var access = new SectionAccess(ReadRoles: [RoleReference.Domain("*")], WriteRoles: [RoleReference.Domain("tenant:admin")]);
        return new FormDefinition(
            Id: new FormDefinitionId(id),
            Version: new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Draft,
            Tenant: Tenant,
            Owner: IdentityRef.System,
            SchemaRef: new SchemaId("sha256:gate-names"),
            Overlay: new HarborlineOverlay(
                Fields: fields,
                Sections: [new FormSection("sec", InternationalizedText.FromInvariant("sec"), ["name"], access)],
                Rules: []),
            Lineage: null,
            CreatedAt: Now,
            UpdatedAt: Now,
            SubmitGate: gate,
            RecordTypeId: target);
    }

    private sealed class FormOnlyReader : IFormRecordTypePolicyReader
    {
        public ValueTask<RecordTypeCreationGate?> GetCreationGateAsync(TenantId tenant, string recordTypeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RecordTypeCreationGate?>(RecordTypeCreationGate.FormOnly);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
