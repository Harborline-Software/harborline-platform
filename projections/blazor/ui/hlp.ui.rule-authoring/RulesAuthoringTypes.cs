using Harborline.Contracts.Forms;
using Harborline.Foundation.RuleAuthoring;

namespace Harborline.UIAdapters.Blazor.Components.RuleAuthoring;

public sealed record RulesPaletteItem(string Id, string Label, ColumnValueType ValueType)
{
    /// <summary>The references of a palette generated from the register and Records fields (rules-auth-20).</summary>
    public static IReadOnlyList<RulesPaletteItem> From(RulesPalette palette)
        => [.. palette.References.Select(reference => new RulesPaletteItem(reference.Id, reference.Label, reference.ValueType))];
}
public sealed record RulesExpressionContract(string Site, string ReturnContract, string ExecutionTimeContract, IReadOnlyList<RulesPaletteItem> Palette);
public sealed record RulesReleaseBinding(string Tenant, string DefinitionId, string VersionId, string WinningWatermark, string CanonicalSource);
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
    public static RulesDraft Empty { get; } = new("", "", "", "Draft", "", new FormulaDraft
    {
        Scope = RuleScope.Field,
        ScopeTarget = "",
        OutputType = RuleActionKind.Compute,
        Inputs = [],
        Expression = new FormulaExpr.Literal("", ColumnValueType.Text),
    });

    public RulesMaterialization? Materialization { get; init; }
}

public sealed record RulesOutcome(string Kind, string InputLabel, string ClockUtc, string? Value = null, string? Code = null, string? RuleName = null, string? MemberName = null, string? RequestId = null, string? Identity = null, string? ExpectedRevision = null, int? Generation = null, string? Validity = null, string? Visibility = null, string? Presentation = null);
public sealed record RulesOperationRequest(string Operation, string RequestId, string Identity, string ExpectedRevision, int Generation, RulesDraft Draft);
public sealed record RulesAuthoritativeState(string Identity, string Revision, string Status);
public sealed record RulesOperationResponse(string RequestId, string Identity, string ExpectedRevision, int Generation, RulesAuthoritativeState? Authoritative = null, RulesOutcome? Outcome = null, RulesMaterialization? Materialization = null);
