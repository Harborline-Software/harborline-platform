using System.Reflection;
using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Assistance;
using Xunit;

namespace Harborline.Foundation.Assistance.Tests;

public sealed class AssistanceDefinitionTests
{
    private const string Tenant = "tenant-a";
    private const string Key = "assistance.customer";

    [Fact(DisplayName = "pilot-ck-2 through pilot-ck-14: every Assistance member round-trips through canonical JSON")]
    public void Canonical_json_round_trips_byte_identically()
    {
        var definition = Definition();
        var first = AssistanceDefinitionJson.SerializeCanonical(definition);
        var parsed = AssistanceDefinitionJson.Deserialize(first);
        var second = AssistanceDefinitionJson.SerializeCanonical(parsed);

        Assert.Equal(first, second);
        Assert.Equal((byte)'\n', first[^1]);
        Assert.Empty(AssistanceDefinitionAdmission.Validate(parsed, AssistanceAdmissionPhase.Install, Catalogue()));
        Assert.Equal("provider-a", parsed.Provider.ProviderId);
        Assert.Equal("ap", JsonDocument.Parse(first).RootElement.GetProperty("commands")[0].GetProperty("classification").GetProperty("tier").GetString());
        Assert.Equal("restricted", parsed.Envelope!.RetentionClass);
        Assert.False(parsed.Envelope.LegalHold);
    }

    [Fact(DisplayName = "pilot-auth-9: undeclared commands refuse, then a registered counterpart admits")]
    public void Unregistered_command_refuses_then_registered_command_admits()
    {
        var refused = AssistanceDefinitionAdmission.Validate(Definition() with { Commands = [Command("missing")] }, AssistanceAdmissionPhase.Author, Catalogue());
        var admitted = AssistanceDefinitionAdmission.Validate(Definition(), AssistanceAdmissionPhase.Author, Catalogue());

        Assert.Contains(refused, refusal => refusal.Code == "definition.command_unregistered");
        Assert.Empty(admitted);
    }

    [Fact(DisplayName = "pilot-ck-8 and pilot-auth-10: tier lowering refuses at Author, then raising admits")]
    public void Tier_lowering_refuses_at_author_then_raising_admits()
    {
        var prior = Definition(AssistanceClassificationTier.Cp);
        var refused = AssistanceDefinitionAdmission.Validate(Definition(AssistanceClassificationTier.Ap), AssistanceAdmissionPhase.Author, Catalogue(), prior);
        var admitted = AssistanceDefinitionAdmission.Validate(Definition(AssistanceClassificationTier.Never), AssistanceAdmissionPhase.Author, Catalogue(), prior);

        Assert.Contains(refused, refusal => refusal.Code == "definition.tier_lowered");
        Assert.Empty(admitted);
    }

    [Fact(DisplayName = "pilot-ck-8 and pilot-auth-10: tier lowering refuses at Install, then raising admits")]
    public void Tier_lowering_refuses_at_install_then_raising_admits()
    {
        var prior = Definition(AssistanceClassificationTier.Never);
        var refused = AssistanceDefinitionAdmission.Validate(Definition(AssistanceClassificationTier.Cp), AssistanceAdmissionPhase.Install, Catalogue(), prior);
        var admitted = AssistanceDefinitionAdmission.Validate(Definition(AssistanceClassificationTier.Never), AssistanceAdmissionPhase.Install, Catalogue(), prior);

        Assert.Contains(refused, refusal => refusal.Code == "definition.tier_lowered");
        Assert.Empty(admitted);
    }

    [Fact(DisplayName = "pilot-auth-10: looser args schema refuses, then equal schema admits")]
    public void Args_schema_widening_refuses_then_equal_schema_admits()
    {
        var prior = Definition(schema: Schema("customerId", "reason"));
        var refused = AssistanceDefinitionAdmission.Validate(Definition(schema: Schema("customerId")), AssistanceAdmissionPhase.Install, Catalogue(), prior);
        var admitted = AssistanceDefinitionAdmission.Validate(Definition(schema: Schema("customerId", "reason")), AssistanceAdmissionPhase.Install, Catalogue(), prior);

        Assert.Contains(refused, refusal => refusal.Code == "definition.args_schema_widened");
        Assert.Empty(admitted);
    }

    [Fact(DisplayName = "pilot-auth-10: wider recipient set refuses, then equal set admits")]
    public void Recipient_widening_refuses_then_equal_set_admits()
    {
        var prior = Definition(recipients: ["support"]);
        var refused = AssistanceDefinitionAdmission.Validate(Definition(recipients: ["support", "manager"]), AssistanceAdmissionPhase.Install, Catalogue(), prior);
        var admitted = AssistanceDefinitionAdmission.Validate(Definition(recipients: ["support"]), AssistanceAdmissionPhase.Install, Catalogue(), prior);

        Assert.Contains(refused, refusal => refusal.Code == "definition.recipients_widened");
        Assert.Empty(admitted);
    }

    [Fact(DisplayName = "pilot-ck-9 and pilot-auth-11: never without archetype refuses, then archetype admits")]
    public void Never_without_archetype_refuses_then_valid_counterpart_admits()
    {
        var refused = AssistanceDefinitionAdmission.Validate(Definition(AssistanceClassificationTier.Never, archetype: null), AssistanceAdmissionPhase.Author, Catalogue());
        var admitted = AssistanceDefinitionAdmission.Validate(Definition(AssistanceClassificationTier.Never), AssistanceAdmissionPhase.Author, Catalogue());

        Assert.Contains(refused, refusal => refusal.Code == "definition.never_archetype_required");
        Assert.Empty(admitted);
    }

    [Fact(DisplayName = "pilot-ck-9 and pilot-auth-11: never without justification refuses, then justification admits")]
    public void Never_without_justification_refuses_then_valid_counterpart_admits()
    {
        var refused = AssistanceDefinitionAdmission.Validate(Definition(AssistanceClassificationTier.Never, justification: " "), AssistanceAdmissionPhase.Author, Catalogue());
        var admitted = AssistanceDefinitionAdmission.Validate(Definition(AssistanceClassificationTier.Never), AssistanceAdmissionPhase.Author, Catalogue());

        Assert.Contains(refused, refusal => refusal.Code == "definition.never_justification_required");
        Assert.Empty(admitted);
    }

    [Fact(DisplayName = "pilot-ck-9: unknown never archetype refuses, then known archetype admits")]
    public void Unknown_never_archetype_refuses_then_valid_counterpart_admits()
    {
        var refused = AssistanceDefinitionAdmission.Validate(Definition(AssistanceClassificationTier.Never, archetype: "not-a-real-archetype"), AssistanceAdmissionPhase.Author, Catalogue());
        var admitted = AssistanceDefinitionAdmission.Validate(Definition(AssistanceClassificationTier.Never), AssistanceAdmissionPhase.Author, Catalogue());

        Assert.Contains(refused, refusal => refusal.Code == "definition.never_archetype_unknown");
        Assert.Empty(admitted);
    }

    [Fact(DisplayName = "pilot-ck-20 and pilot-auth-12: AssistanceDefinition exposes no prompt or instruction-text property")]
    public void Assistance_definition_has_no_free_text_prompt_or_instruction_property()
    {
        var forbidden = typeof(AssistanceDefinition).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(string))
            .Where(property => property.Name.Contains("prompt", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("instruction", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("text", StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Name);

        Assert.Empty(forbidden);
    }

    [Fact]
    public void Non_object_args_schema_refuses()
    {
        var definition = Definition(schema: JsonDocument.Parse("[]").RootElement.Clone());
        Assert.Contains(AssistanceDefinitionAdmission.Validate(definition, AssistanceAdmissionPhase.Author, Catalogue()), refusal => refusal.Code == "definition.args_schema_not_object");
    }

    [Fact(DisplayName = "duplicate CommandId values refuse instead of admitting")]
    public void Duplicate_command_id_refuses()
    {
        var definition = Definition() with { Commands = [Command("customer.update"), Command("customer.update")] };
        Assert.Contains(AssistanceDefinitionAdmission.Validate(definition, AssistanceAdmissionPhase.Author, Catalogue()), refusal => refusal.Code == "definition.command_duplicate");
    }

    [Fact(DisplayName = "narrowing against a previous definition with duplicate CommandId values does not throw")]
    public void Narrowing_against_previous_with_duplicate_command_ids_does_not_throw()
    {
        // Only the previous definition carries the duplicate; ToDictionary used to throw
        // ArgumentException building the narrowing lookup regardless of whose duplicate it was.
        var previousWithDuplicates = Definition() with { Commands = [Command("customer.update"), Command("customer.update")] };
        var exception = Record.Exception(() =>
            AssistanceDefinitionAdmission.Validate(Definition(), AssistanceAdmissionPhase.Install, Catalogue(), previousWithDuplicates));
        Assert.Null(exception);
    }

    [Fact(DisplayName = "a body missing a required member refuses definition.body_invalid instead of throwing")]
    public void Missing_required_member_refuses_instead_of_throwing()
    {
        var body = "{\"tenant\":\"tenant-a\",\"key\":\"assistance.customer\",\"version\":\"1.0.0\",\"surface\":\"customer\",\"route\":\"/customers/{customerId}\",\"commands\":[],\"context_allowlist\":[],\"recipients\":[\"support\"]}";
        // "provider" is required and absent.
        Assert.Contains(AssistanceDefinitionAdmission.AdmitJson(body, AssistanceAdmissionPhase.Author, Catalogue()), refusal => refusal.Code == "definition.body_invalid");
    }

    [Fact(DisplayName = "an explicit null for a required member refuses definition.body_invalid instead of throwing")]
    public void Null_required_member_refuses_instead_of_throwing()
    {
        var body = "{\"tenant\":\"tenant-a\",\"key\":\"assistance.customer\",\"version\":\"1.0.0\",\"surface\":\"customer\",\"route\":\"/customers/{customerId}\",\"commands\":null,\"context_allowlist\":[],\"recipients\":[\"support\"],\"provider\":{\"provider_id\":\"provider-a\",\"model_id\":\"model-a\"}}";
        Assert.Contains(AssistanceDefinitionAdmission.AdmitJson(body, AssistanceAdmissionPhase.Author, Catalogue()), refusal => refusal.Code == "definition.body_invalid");
    }

    [Fact(DisplayName = "a command missing args_schema refuses instead of throwing when narrowed against a previous definition")]
    public void Missing_args_schema_refuses_instead_of_throwing_during_narrowing()
    {
        var body = "{\"tenant\":\"tenant-a\",\"key\":\"assistance.customer\",\"version\":\"1.0.0\",\"surface\":\"customer\",\"route\":\"/customers/{customerId}\",\"commands\":[{\"command_id\":\"customer.update\",\"aliases\":[],\"classification\":{\"tier\":\"ap\",\"undoable\":true}}],\"context_allowlist\":[],\"recipients\":[\"support\"],\"provider\":{\"provider_id\":\"provider-a\",\"model_id\":\"model-a\"}}";
        // "args_schema" is required and absent on the command. A previous definition naming the same
        // command id used to reach ValidateNarrowing's GetRawText() call on the default JsonElement
        // and throw InvalidOperationException instead of refusing.
        Assert.Contains(AssistanceDefinitionAdmission.AdmitJson(body, AssistanceAdmissionPhase.Install, Catalogue(), catalogue: null, previous: Definition()), refusal => refusal.Code == "definition.body_invalid");
    }

    [Fact]
    public void Store_adapter_targets_the_reserved_pilot_namespace()
    {
        var document = new DefinitionDocument(new(Tenant, DefinitionKind.Pilot, Key), "1.0.0", "1.0.0", System.Text.Encoding.UTF8.GetString(AssistanceDefinitionJson.SerializeCanonical(Definition())));
        Assert.Empty(AssistanceDefinitionStoreAdmission.Admit(document, DefinitionAdmissionPhase.Publish, Catalogue()));
    }

    private static AssistanceDefinition Definition(AssistanceClassificationTier tier = AssistanceClassificationTier.Ap, JsonElement? schema = null, IReadOnlyList<string>? recipients = null, string? archetype = "security-access-control", string? justification = "Human review is mandatory.") => new(
        Tenant, Key, "1.0.0", "customer", "/customers/{customerId}", [Command("customer.update", tier, schema, archetype, justification)],
        [new("records.customer", "status", AssistanceContextRedaction.Value)], recipients ?? ["support"], new("provider-a", "model-a"), Envelope: new(Key, "1.0.0", Tenant, AssistanceCascadeLayer.Tenant, JsonDocument.Parse("{\"source\":\"tenant\"}").RootElement.Clone(), "restricted", false, []));

    private static AssistanceCommand Command(string commandId, AssistanceClassificationTier tier = AssistanceClassificationTier.Ap, JsonElement? schema = null, string? archetype = "security-access-control", string? justification = "Human review is mandatory.")
        => new(commandId, ["update customer"], schema ?? Schema("customerId"), new(tier, true, archetype, justification));

    private static JsonElement Schema(params string[] required) => JsonDocument.Parse($"{{\"type\":\"object\",\"required\":[{string.Join(',', required.Select(item => $"\"{item}\""))}]}}").RootElement.Clone();
    private static ICommandCatalogueRegistry Catalogue() => new TestCatalogue();

    private sealed class TestCatalogue : ICommandCatalogueRegistry
    {
        public CommandCatalogueEntry? Resolve(string commandId) => commandId == "customer.update" ? new(commandId) : null;
    }
}
