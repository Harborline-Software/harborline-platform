using System.Collections.Frozen;
using Harborline.Contracts.Forms;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The developer-supplied registers a host admits Layout content against: DES-0052's bound
/// inventory. Admission checks every name a surface cites against them and invents none.
/// </summary>
/// <param name="Kinds">The block-kind register (layout-bound-1).</param>
/// <param name="FieldControls">The field controls a capture block may pick (layout-bound-3). Absent, no named control admits.</param>
/// <param name="Pages">The page layouts and masters installed packs supply (layout-bound-7). Absent, a surface cites only its own.</param>
/// <param name="ValidationRules">The named validation rules a capture block may cite (layout-bound-8). Absent, rule names are checked for shape only.</param>
public sealed record LayoutHostRegisters(
    LayoutBlockKindRegistry Kinds,
    LayoutFieldControlRegistry? FieldControls = null,
    LayoutPageRegistry? Pages = null,
    LayoutValidationRuleRegistry? ValidationRules = null)
{
    /// <summary>The platform's block grammar and no other register.</summary>
    public static LayoutHostRegisters Platform { get; } = new(LayoutBlockKindRegistry.Platform);
}

/// <summary>
/// DES-0052 layout-bound-3 — the field controls a host registers for capture blocks. A capture
/// block names one of these and parameterises it; admission refuses a control not registered.
/// </summary>
public sealed class LayoutFieldControlRegistry
{
    private readonly FrozenSet<string> _controls;

    /// <summary>Creates an immutable register from nonempty control identifiers.</summary>
    /// <param name="controls">The registered control identifiers, such as the schema-form control hints.</param>
    public LayoutFieldControlRegistry(IEnumerable<string> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        _controls = controls.ToFrozenSet(StringComparer.Ordinal);
        if (_controls.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A field-control register requires nonempty identifiers.", nameof(controls));
    }

    /// <summary>Returns whether the host registered the control.</summary>
    /// <param name="control">The exact control identifier.</param>
    public bool Contains(string control) => _controls.Contains(control);
}

/// <summary>
/// DES-0052 layout-bound-8 — the named validation rules a host registers for capture blocks. Each
/// is a Rules definition whose own tier decides which compiler admits it; Layout picks none.
/// </summary>
public sealed class LayoutValidationRuleRegistry
{
    private readonly FrozenDictionary<string, RuleDefinition> _rules;

    /// <summary>Creates a register keyed by each rule's unique identifier.</summary>
    /// <param name="rules">The registered validation rules.</param>
    public LayoutValidationRuleRegistry(IEnumerable<RuleDefinition> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var values = rules.ToArray();
        if (values.Any(rule => rule is null || string.IsNullOrWhiteSpace(rule.Id)))
            throw new ArgumentException("A validation-rule register requires identified rules.", nameof(rules));
        _rules = values.ToFrozenDictionary(rule => rule.Id, StringComparer.Ordinal);
    }

    /// <summary>Looks up one registered rule by its exact name.</summary>
    /// <param name="name">The rule name a capture block cites.</param>
    /// <param name="rule">The registered rule, when found.</param>
    public bool TryGet(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RuleDefinition? rule)
        => _rules.TryGetValue(name, out rule);
}

/// <summary>
/// DES-0052 layout-bound-7 — the page layouts and page masters installed packs supply as reusable
/// definitions (layout-ck-18, layout-ck-19). A page run cites them by id instead of copying them.
/// </summary>
public sealed class LayoutPageRegistry
{
    /// <summary>Creates a register of uniquely identified definitions whose masters sit over registered layouts.</summary>
    /// <param name="layouts">The supplied page geometries.</param>
    /// <param name="masters">The supplied page masters.</param>
    public LayoutPageRegistry(IEnumerable<LayoutPageLayoutDefinition> layouts, IEnumerable<LayoutPageMasterDefinition> masters)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(masters);
        var layoutValues = layouts.ToArray();
        var masterValues = masters.ToArray();
        if (layoutValues.Any(layout => layout is null || string.IsNullOrWhiteSpace(layout.Id))
            || masterValues.Any(master => master is null || string.IsNullOrWhiteSpace(master.Id)))
            throw new ArgumentException("A page register requires identified definitions.");
        Layouts = layoutValues.ToFrozenDictionary(layout => layout.Id, StringComparer.Ordinal);
        Masters = masterValues.ToFrozenDictionary(master => master.Id, StringComparer.Ordinal);
        if (masterValues.Any(master => !Layouts.ContainsKey(master.PageLayoutId)))
            throw new ArgumentException("Every supplied page master must sit over a supplied page layout.", nameof(masters));
    }

    /// <summary>The supplied page geometries, by id.</summary>
    public IReadOnlyDictionary<string, LayoutPageLayoutDefinition> Layouts { get; }

    /// <summary>The supplied page masters, by id.</summary>
    public IReadOnlyDictionary<string, LayoutPageMasterDefinition> Masters { get; }
}
