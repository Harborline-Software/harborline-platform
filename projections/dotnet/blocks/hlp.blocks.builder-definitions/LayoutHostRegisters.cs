using System.Collections.Frozen;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The developer-supplied registers a host admits Layout content against: DES-0052's bound
/// inventory. Admission checks every name a surface cites against them and invents none.
/// </summary>
/// <param name="Kinds">The block-kind register (layout-bound-1).</param>
/// <param name="FieldControls">The field controls a capture block may pick (layout-bound-3). Absent, no named control admits.</param>
public sealed record LayoutHostRegisters(
    LayoutBlockKindRegistry Kinds,
    LayoutFieldControlRegistry? FieldControls = null)
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
