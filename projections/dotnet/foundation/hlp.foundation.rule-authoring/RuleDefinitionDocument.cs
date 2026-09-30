using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.Definitions;

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
    IReadOnlyList<string> Requires,
    DefinitionContractVersion? Contract);

/// <summary>The source language tier declared by a rule document.</summary>
public enum RuleDefinitionTier
{
    /// <summary>JSON Schema validation rules.</summary>
    JsonSchema,
    /// <summary>JsonLogic rules compiled by the rule engine.</summary>
    JsonLogic,
    /// <summary>Power Fx rules reserved for a compatible compiler.</summary>
    PowerFx
}

/// <summary>The named rule's authored source; the compiled AST is derived at admission.</summary>
public sealed record RuleDefinitionDocument(
    RuleDefinitionEnvelope Envelope,
    string Name,
    RuleDefinitionTier Tier,
    RuleDraft Draft);

/// <summary>The lifecycle phase whose admission rules are being applied.</summary>
public enum RuleIntentPhase
{
    /// <summary>The editor authoring phase.</summary>
    Author,
    /// <summary>The publication gate.</summary>
    Publish,
    /// <summary>Validation of persisted source.</summary>
    Persisted
}

/// <summary>One localizable refusal shared by both editor projections. Location is RFC 6901.</summary>
public sealed record RuleIntentDiagnostic(
    string Code,
    string Location,
    RuleIntentPhase Phase,
    string? RuleId = null,
    IReadOnlyList<string>? CyclePath = null);

/// <summary>The result of validating a rule, with an admitted document or stable diagnostics.</summary>
/// <param name="Document">The admitted document, or <c>null</c> when validation refused it.</param>
/// <param name="Diagnostics">Stable diagnostics explaining every refusal.</param>
public sealed record RuleIntentResult(
    RuleDefinitionDocument? Document,
    IReadOnlyList<RuleIntentDiagnostic> Diagnostics)
{
    /// <summary>True when a document was admitted and no diagnostics were produced.</summary>
    public bool IsValid => Document is not null && Diagnostics.Count == 0;

    /// <summary>The admitted rule as the engine lowered it (references rewritten, e.g. <c>parent.z</c> to <c>field.z</c>). Present only when admitted.</summary>
    public JsonNode? Lowered { get; init; }
}

/// <summary>Schema facts consumed by editors and admission; numeric limits have one owner.</summary>
public sealed class RuleIntentSchema
{
    private RuleIntentSchema() { }
    /// <summary>The singleton schema facts exposed to editors and admission.</summary>
    public static RuleIntentSchema Current { get; } = new();
    /// <summary>The engine limits that bound authoring and compilation.</summary>
    public RuleEngineLimits Limits => RuleEngineLimits.Default;
}

/// <summary>Stable refusal codes emitted while reading and validating rule documents.</summary>
public static class RuleDefinitionCodes
{
    /// <summary>The document shape is invalid.</summary>
    public const string InvalidDocument = "rules.definition.invalid_document";
    /// <summary>The source contains an unknown member.</summary>
    public const string UnknownMember = "rules.definition.unknown_member";
    /// <summary>The source contains duplicate members.</summary>
    public const string DuplicateMember = "rules.definition.duplicate_member";
    /// <summary>The version value is invalid.</summary>
    public const string InvalidVersion = "rules.definition.invalid_version";
    /// <summary>The declared tier is unsupported.</summary>
    public const string InvalidTier = "rule.compile.unsupported_tier";
    /// <summary>The scope grammar is invalid.</summary>
    public const string InvalidScope = "rule.compile.bad_grammar";
    /// <summary>The action is not admitted by the compiler.</summary>
    public const string InvalidAction = "rule.compile.unknown_action";
    /// <summary>The decision-table cell kind is invalid.</summary>
    public const string InvalidCellKind = "rule.skin.decision_table_bad_cell";
    /// <summary>A numeric endpoint is invalid.</summary>
    public const string InvalidNumericEndpoint = "rule.skin.decision_table_bad_cell";
    /// <summary>The version policy is invalid.</summary>
    public const string InvalidVersionPolicy = "rules.definition.invalid_version_policy";
}
