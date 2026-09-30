using Harborline.Contracts.Forms;
using Harborline.Foundation.RuleAuthoring;

namespace Harborline.UIAdapters.Blazor.Components.RuleAuthoring;

/// <summary>A draggable item in the rules palette: id, label and the type of value it produces.</summary>
public sealed record RulesPaletteItem(string Id, string Label, ColumnValueType ValueType)
{
    /// <summary>The references of a palette generated from the register and Records fields (rules-auth-20).</summary>
    public static IReadOnlyList<RulesPaletteItem> From(RulesPalette palette)
        => [.. palette.References.Select(reference => new RulesPaletteItem(reference.Id, reference.Label, reference.ValueType))];
}
/// <summary>What an expression site accepts: where it sits, its return and timing contracts and its palette.</summary>
public sealed record RulesExpressionContract(string Site, string ReturnContract, string ExecutionTimeContract, IReadOnlyList<RulesPaletteItem> Palette);
/// <summary>Where a released rule definition is bound: tenant, definition, version, winning watermark and source.</summary>
public sealed record RulesReleaseBinding(string Tenant, string DefinitionId, string VersionId, string WinningWatermark, string CanonicalSource);
/// <summary>The materialised release of a rule: its bindings, canonical content and content digest.</summary>
public sealed record RulesMaterialization(IReadOnlyList<RulesReleaseBinding> Bindings, string CanonicalContent, string ContentDigest);

/// <summary>Lifecycle envelope around the producer-owned <see cref="RuleDraft"/>.</summary>
public sealed record RulesDraft(
    string Identity,
    string ExpectedRevision,
    string Name,
    string VersionSelection,
    string PinnedVersionId,
    RuleDraft Draft)
{
    /// <summary>An empty rules draft in the Draft state.</summary>
    public static RulesDraft Empty { get; } = new("", "", "", "Draft", "", new FormulaDraft
    {
        Scope = RuleScope.Field,
        ScopeTarget = "",
        OutputType = RuleActionKind.Compute,
        Inputs = [],
        Expression = new FormulaExpr.Literal("", ColumnValueType.Text),
    });

    /// <summary>The materialised release attached to the draft, when there is one.</summary>
    public RulesMaterialization? Materialization { get; init; }
}

/// <summary>A field the host exposes to Rules field-property controls.</summary>
public sealed record RulesFieldBinding(string Key, string Label);

/// <summary>The shared catalogue of ordinary editable Rules drafts.</summary>
public sealed record RulesRuleCatalogue(IReadOnlyList<RulesDraft> Rules);

/// <summary>The outcome of a rules operation or test: kind, input, clock, value, code and rule or member involved.</summary>
public sealed record RulesOutcome(string Kind, string InputLabel, string ClockUtc, string? Value = null, string? Code = null, string? RuleName = null, string? MemberName = null, string? RequestId = null, string? Identity = null, string? ExpectedRevision = null, int? Generation = null, string? Validity = null, string? Visibility = null, string? Presentation = null);
/// <summary>A request to run an operation on a rules draft, with the identity, expected revision and generation.</summary>
public sealed record RulesOperationRequest(string Operation, string RequestId, string Identity, string ExpectedRevision, int Generation, RulesDraft Draft);
/// <summary>The server current identity, revision and status for a rules draft.</summary>
public sealed record RulesAuthoritativeState(string Identity, string Revision, string Status);
/// <summary>The server answer to a rules operation: request echo, authoritative state, outcome and materialisation.</summary>
public sealed record RulesOperationResponse(string RequestId, string Identity, string ExpectedRevision, int Generation, RulesAuthoritativeState? Authoritative = null, RulesOutcome? Outcome = null, RulesMaterialization? Materialization = null, IReadOnlyList<RulesRefusal>? Refusals = null);

/// <summary>A producer refusal the editor renders as given: a stable code at an RFC 6901 pointer (DefinitionRefusal).</summary>
public sealed record RulesRefusal(string Code, string Pointer, string? Target = null);

/// <summary>
/// The host's Access verdicts for the acting principal over the Rules capability names (DES-0018 §6). The editor asks
/// and never decides: a name absent from <paramref name="Granted"/> is denied.
/// </summary>
public sealed record RulesEditorAuthority(IReadOnlyList<string> Granted)
{
    /// <summary>Save, restore or archive a draft (<c>rules:author</c>).</summary>
    public bool CanAuthor => Granted.Contains("rules:author", StringComparer.Ordinal);

    /// <summary>Publish a version (<c>rules:publish</c>).</summary>
    public bool CanPublish => Granted.Contains("rules:publish", StringComparer.Ordinal);

    /// <summary>Preview over records, which needs <c>rules:evaluate-explain</c> paired with <c>records:read</c>.</summary>
    public bool CanPreview => Granted.Contains("rules:evaluate-explain", StringComparer.Ordinal) && Granted.Contains("records:read", StringComparer.Ordinal);
}
