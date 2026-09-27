// Namespace deliberately unchanged from these types' original home in hlp.blocks.builder-definitions (blocks
// tier). Owner ruling Q48 / T-724 ruling 116 requires their full namespace, name, generic shape, accessibility and
// public contract preserved so [TypeForwardedTo] in that assembly resolves them by exact type name and every
// existing compiled consumer keeps resolving the same runtime type without a source change. This file is the
// only place these four types are defined; do not add a second copy under Harborline.Foundation.Definitions.
namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The admission boundary presented to a member's validator.</summary>
public enum DefinitionAdmissionPhase
{
    /// <summary>Draft creation, replacement or restoration.</summary>
    Author,
    /// <summary>Publication of an immutable version.</summary>
    Publish,
    /// <summary>Installation of released content against its pinned dependency closure.</summary>
    Install,
    /// <summary>Re-admission of persisted published content against this host before it renders (T-583 item 2).</summary>
    Render,
}

/// <summary>
/// A stable, localizable refusal at an RFC 6901 pointer. <paramref name="Target"/> names a fetchable definition the
/// refusal concerns, and is present only when revealing it is safe and authorized; otherwise it is omitted.
/// </summary>
public sealed record DefinitionRefusal(string Code, string Pointer, string? Target = null);

/// <summary>A pure admission verdict: the stage it ran at and every refusal, empty when admitted.</summary>
public sealed record DefinitionRefusalReport(DefinitionAdmissionPhase Stage, IReadOnlyList<DefinitionRefusal> Refusals);

/// <summary>A refused operation. No history or published head changed.</summary>
public sealed class DefinitionRefusalException : Exception
{
    /// <summary>Captures a detached refusal list and the stage that refused.</summary>
    public DefinitionRefusalException(DefinitionAdmissionPhase stage, IEnumerable<DefinitionRefusal> refusals)
        : base("definition.refused") => (Stage, Refusals) = (stage, Array.AsReadOnly(refusals.ToArray()));

    /// <summary>The admission stage that refused. There is no default: every refusal names its stage explicitly (T-724 ruling 79).</summary>
    public DefinitionAdmissionPhase Stage { get; }

    /// <summary>The coded, located reasons for refusal.</summary>
    public IReadOnlyList<DefinitionRefusal> Refusals { get; }
}
