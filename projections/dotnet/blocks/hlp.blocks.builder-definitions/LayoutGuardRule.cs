using Harborline.Contracts.Forms;
using Harborline.Foundation.RuleEngine.References;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The Rules definition a block's <c>show_when</c> guard compiles as (layout-ck-29, layout-eng-16).
/// Publication compiles it (T-724 ruling 39) and the runtime evaluates it, from this one shape.
/// </summary>
public static class LayoutGuardRule
{
    private static readonly PinnedClosure NoPredicates = new([], []);

    /// <summary>The consumer kind a Layout guard binds its named predicate as (T-724 ruling 72).</summary>
    public const PredicateConsumer Consumer = PredicateConsumer.LayoutGuard;

    /// <summary>
    /// Builds the guard rule for one block's declared guard. A predicate resolves only through
    /// <paramref name="predicates"/>, the closure the definition was published with.
    /// </summary>
    /// <exception cref="NamedReferenceException">The guard holds neither form or both, or its pin does not resolve.</exception>
    public static RuleDefinition For(string blockId, LayoutShowWhen guard, string? rowSection, PinnedClosure? predicates)
    {
        ArgumentNullException.ThrowIfNull(guard);
        if (!guard.IsWellFormed)
            throw new NamedReferenceException(NamedReferences.Malformed, "show_when holds exactly one of expression or predicate");
        var expression = guard.Predicate is { } pin
            ? NamedReferences.Bind(Consumer, pin, predicates ?? NoPredicates).Expression
            : guard.Expression!;
        return For(blockId, expression, rowSection);
    }

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
