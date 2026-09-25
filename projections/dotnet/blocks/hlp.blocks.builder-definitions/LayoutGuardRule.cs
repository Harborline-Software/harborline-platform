using Harborline.Contracts.Forms;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The Rules definition a block's <c>show_when</c> guard compiles as (layout-ck-29, layout-eng-16).
/// Publication compiles it (T-724 ruling 39) and the runtime evaluates it, from this one shape.
/// </summary>
public static class LayoutGuardRule
{
    /// <summary>Builds the guard rule for one block.</summary>
    /// <param name="blockId">The guarded block.</param>
    /// <param name="expression">The authored Rules expression.</param>
    /// <param name="rowSection">The repeating collection whose rows the block resolves in, or <see langword="null"/> at the surface root.</param>
    public static RuleDefinition For(string blockId, string expression, string? rowSection) => new()
    {
        Id = $"layout.show_when.{blockId}",
        Tier = RuleTier.JsonLogic,
        // A block inside a repeating container is a Row-scoped rule over that container's section,
        // which is what makes a `row.` reference legal there and illegal anywhere else. Its target
        // is 'section/field': the repeating collection and the block within one row.
        Scope = rowSection is null ? RuleScope.Schema : RuleScope.Row,
        ScopeTarget = rowSection is null ? blockId : $"{rowSection}/{blockId}",
        Expression = expression,
        Action = RuleActionKind.Validate,
    };
}
