using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine;

namespace Harborline.Foundation.RuleAuthoring;

/// <summary>
/// Provider-neutral identity metadata. Policy-owned retention and legal hold are outside authoring
/// until their governing record correction is resolved (T-588 item 4).
/// Version labels are preserved here; the shared definition store owns semantic-version admission.
/// </summary>
public sealed record RuleDefinitionEnvelope(
    string Id,
    string Version,
    string Tenant,
    string CascadeLayer,
    JsonObject Provenance,
    IReadOnlyList<string> Requires);

public enum RuleDefinitionTier { JsonSchema, JsonLogic, PowerFx }

/// <summary>The named rule's authored source; the compiled AST is derived at admission.</summary>
public sealed record RuleDefinitionDocument(
    RuleDefinitionEnvelope Envelope,
    string Name,
    RuleDefinitionTier Tier,
    RuleDraft Draft);

public enum RuleIntentPhase { Author, Publish, Persisted }

/// <summary>One localizable refusal shared by both editor projections. Location is RFC 6901.</summary>
public sealed record RuleIntentDiagnostic(
    string Code,
    string Location,
    RuleIntentPhase Phase,
    string? RuleId = null,
    IReadOnlyList<string>? CyclePath = null);

public sealed record RuleIntentResult(
    RuleDefinitionDocument? Document,
    IReadOnlyList<RuleIntentDiagnostic> Diagnostics)
{
    public bool IsValid => Document is not null && Diagnostics.Count == 0;
}

/// <summary>Schema facts consumed by editors and admission; numeric limits have one owner.</summary>
public sealed class RuleIntentSchema
{
    private RuleIntentSchema() { }
    public static RuleIntentSchema Current { get; } = new();
    public RuleEngineLimits Limits => RuleEngineLimits.Default;
}

public static class RuleDefinitionCodes
{
    public const string InvalidDocument = "rules.definition.invalid_document";
    public const string UnknownMember = "rules.definition.unknown_member";
    public const string DuplicateMember = "rules.definition.duplicate_member";
    public const string InvalidVersion = "rules.definition.invalid_version";
    public const string InvalidTier = "rule.compile.unsupported_tier";
    public const string InvalidScope = "rule.compile.bad_grammar";
    public const string InvalidAction = "rule.compile.unknown_action";
    public const string InvalidCellKind = "rule.skin.decision_table_bad_cell";
    public const string InvalidNumericEndpoint = "rule.skin.decision_table_bad_cell";
    public const string InvalidVersionPolicy = "rules.definition.invalid_version_policy";
}
