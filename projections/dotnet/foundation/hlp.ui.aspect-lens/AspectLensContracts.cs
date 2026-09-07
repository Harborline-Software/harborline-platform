namespace Harborline.Foundation.Builder;

/// <summary>A host-owned node projected into the shared builder outline.</summary>
public sealed record CanvasNode(
    string Id,
    string Kind,
    string Label,
    int Depth,
    string? ParentId,
    string? Detail = null,
    bool? HasChildren = null);

/// <summary>The ordered outline and host selection seam.</summary>
public sealed record CanvasModel(
    IReadOnlyList<CanvasNode> Nodes,
    string? SelectedId,
    Action<string> Select);

/// <summary>Semantic lens tones; renderers map these values to their own theme tokens.</summary>
public enum LensTone
{
    /// <summary>Primary dependency or logic emphasis.</summary>
    Accent,
    /// <summary>Warning state.</summary>
    Warning,
    /// <summary>Danger or access state.</summary>
    Danger,
    /// <summary>Successful or satisfied state.</summary>
    Success,
    /// <summary>Neutral informational state.</summary>
    Info,
    /// <summary>Muted base state.</summary>
    Muted,
    /// <summary>No sensitivity classification.</summary>
    SensitivityNone,
    /// <summary>Low sensitivity classification.</summary>
    SensitivityLow,
    /// <summary>Medium sensitivity classification.</summary>
    SensitivityMedium,
    /// <summary>High sensitivity classification.</summary>
    SensitivityHigh,
}

/// <summary>Whether a lens colors participating nodes or ghosts non-participants.</summary>
public enum AspectLensKind
{
    /// <summary>Color participating nodes.</summary>
    Colorize,
    /// <summary>Ghost non-participating nodes while retaining context.</summary>
    Filter,
}

/// <summary>A lens projection for one host node.</summary>
public sealed record AspectState(
    bool Active,
    string? Badge = null,
    LensTone? Tone = null,
    string? Title = null);

/// <summary>A directed non-tree relationship surfaced by a lens.</summary>
public sealed record AspectEdge(string From, string To, string? Label = null);

/// <summary>Optional light-edit descriptor owned by the host.</summary>
public sealed record AspectLensEditor(string Hint);

/// <summary>Framework-neutral aspect projection used by Forms and Workflow builders.</summary>
public sealed record AspectLens(
    string Id,
    string Label,
    LensTone Tone,
    AspectLensKind Kind,
    Func<string, AspectState> Project,
    Func<IReadOnlyList<AspectEdge>>? Edges = null,
    AspectLensEditor? Editor = null,
    string? Empty = null);

/// <summary>Closed configuration-cascade source vocabulary.</summary>
public enum ProvenanceSource
{
    /// <summary>Base template.</summary>
    Base,
    /// <summary>Domain pack.</summary>
    Pack,
    /// <summary>Tenant customization.</summary>
    Tenant,
    /// <summary>Instance override.</summary>
    Instance,
    /// <summary>Fail-closed unresolved source.</summary>
    Unknown,
}

/// <summary>Resolved configuration provenance for one node.</summary>
public sealed record ProvenanceInfo(
    ProvenanceSource Source,
    IReadOnlyList<ProvenanceSource> Chain,
    bool Overridden,
    bool Locked,
    bool Resolved);

/// <summary>Host adapter that resolves configuration provenance.</summary>
public sealed record ProvenanceResolver(Func<string, ProvenanceInfo> Resolve);
