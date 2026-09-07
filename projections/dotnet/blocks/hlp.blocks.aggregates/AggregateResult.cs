namespace Harborline.Blocks.Aggregates;

/// <summary>Cell availability state.</summary>
public enum AggregateCellState
{
    /// <summary>A concrete value.</summary>
    Value,
    /// <summary>A valid null result.</summary>
    Null,
    /// <summary>An explicitly unavailable result.</summary>
    Unavailable,
}
/// <summary>Result group kind.</summary>
public enum AggregateGroupKind
{
    /// <summary>Full-key detail group.</summary>
    Detail,
    /// <summary>Grouping-prefix subtotal.</summary>
    Subtotal,
    /// <summary>All-row total.</summary>
    GrandTotal,
}

/// <summary>Complete result from one source snapshot.</summary>
/// <param name="DefinitionId">Definition identifier.</param><param name="Revision">Definition revision.</param><param name="Snapshot">Opaque source snapshot token.</param><param name="Groups">Deterministically ordered result groups.</param>
public sealed record AggregateResult(string DefinitionId, long Revision, string Snapshot, IReadOnlyList<AggregateGroup> Groups);
/// <summary>One detail or total group.</summary>
/// <param name="Kind">Group kind.</param><param name="Level">Grouping-prefix depth.</param><param name="Keys">Ordered typed keys.</param><param name="Measures">Declaration-ordered cells.</param>
public sealed record AggregateGroup(AggregateGroupKind Kind, int Level, IReadOnlyList<AggregateKey> Keys, IReadOnlyList<AggregateCell> Measures);
/// <summary>Typed group key.</summary><param name="Key">Dimension result key.</param><param name="Type">Value type.</param><param name="Value">Canonical value or null.</param>
public sealed record AggregateKey(string Key, AggregateValueType Type, object? Value);
/// <summary>Typed result cell.</summary><param name="Key">Measure result key.</param><param name="Type">Result type.</param><param name="State">Cell state.</param><param name="Value">Canonical value when state is value.</param>
public sealed record AggregateCell(string Key, AggregateValueType Type, AggregateCellState State, object? Value);

/// <summary>Stable aggregate failure.</summary>
public sealed class AggregateException : Exception
{
    /// <summary>Creates a coded failure.</summary><param name="code">Stable problem code.</param><param name="message">Diagnostic message.</param>
    public AggregateException(string code, string message) : base(message) => Code = code;
    /// <summary>Stable problem code.</summary>
    public string Code { get; }
}
