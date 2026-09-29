namespace Harborline.Foundation.ExecutionRuntime;

/// <summary>
/// The one run identity every engine's run carries. The kind is part of the identity, so a
/// <c>workflow-run</c> and a <c>plan-run</c> never share one even when their opaque values coincide;
/// correlation between runs is <see cref="RunRecord.CausedBy"/>, never a merged identity
/// (DES-0056 <c>execution-runtime-cc-3</c>, <c>execution-runtime-cc-5</c>).
/// </summary>
public readonly record struct RunId
{
    /// <summary>Creates a run identity from its kind and opaque value.</summary>
    public RunId(RunKind kind, Guid value)
    {
        if (kind.Value is null)
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.RunKindInvalid, "A run identity requires a run kind.");
        }

        if (value == Guid.Empty)
        {
            throw new ArgumentException("A run identity requires a non-empty value.", nameof(value));
        }

        Kind = kind;
        Value = value;
    }

    /// <summary>The registered kind the run belongs to.</summary>
    public RunKind Kind { get; }

    /// <summary>The opaque value, unique within the kind.</summary>
    public Guid Value { get; }

    /// <summary>Issues a fresh identity for a kind.</summary>
    public static RunId New(RunKind kind) => new(kind, Guid.NewGuid());

    /// <summary>The wire form, <c>kind:value</c>.</summary>
    public override string ToString() => $"{Kind}:{Value:D}";
}
