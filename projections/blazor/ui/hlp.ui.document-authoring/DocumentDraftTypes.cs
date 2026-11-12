namespace Harborline.UIAdapters.Blazor.Components.DocumentAuthoring;

/// <summary>A host-offered identifier and its accessible label.</summary>
public sealed record DocumentOption(string Id, string Label)
{
    /// <summary>A bare identifier is also its label.</summary>
    public static implicit operator DocumentOption(string id) => new(id, id);
}
/// <summary>Host catalogues; absent collections render explicit fallback sections.</summary>
public sealed record DocumentCatalogue
{
    /// <summary>Record field paths.</summary>
    public IReadOnlyList<DocumentOption> Fields { get; init; } = [];
    /// <summary>Layout static-region identifiers.</summary>
    public IReadOnlyList<DocumentOption> Regions { get; init; } = [];
    /// <summary>Records offered for preview.</summary>
    public IReadOnlyList<DocumentOption> Records { get; init; } = [];
    /// <summary>Labelled samples offered for non-minting preview.</summary>
    public IReadOnlyList<DocumentOption> Samples { get; init; } = [];
}
/// <summary>Layout's literal-or-field text run, with a fallback only on a field.</summary>
public sealed record DocumentRun(string? Text = null, string? FieldPath = null, string? Fallback = null);
/// <summary>An ordered line of Layout text runs.</summary>
public sealed record DocumentLine(IReadOnlyList<DocumentRun> Runs);
/// <summary>Authored text and semantic page controls; geometry remains in Layout.</summary>
public sealed record DocumentBlock(string Id, IReadOnlyList<DocumentLine> Lines, string? CollapseBelow = null, bool BreakBefore = false, bool BreakAfter = false, string BreakInside = "auto");
/// <summary>Layout's static-region assignment for one page-master variant.</summary>
public sealed record DocumentMasterVariant(string? LeftRegion = null, string? CenterRegion = null, string? RightRegion = null);
/// <summary>The first, left and right variants of a host-pinned Layout page master.</summary>
public sealed record DocumentMasters(DocumentMasterVariant First, DocumentMasterVariant Left, DocumentMasterVariant Right);
/// <summary>The document locale, independent of the editor's UI locale.</summary>
public sealed record DocumentLocale(string Kind, string? Tag = null);
/// <summary>Authored masthead style, never product chrome.</summary>
public sealed record DocumentStyle(string? BrandName = null, string? LogoAssetRef = null);
/// <summary>Local editor state. The host owns envelope, record binding, surface pin and persistence.</summary>
public sealed record DocumentDraft(string Identity, string? Revision, string Name, string DocumentType, DocumentLocale Locale, DocumentStyle Style, IReadOnlyList<DocumentBlock> Blocks, DocumentMasters Masters)
{
    /// <summary>An unpersisted template created from nothing.</summary>
    public static DocumentDraft Empty => new("", null, "", "", new("from_instance"), new(), [], new(new(), new(), new()));
}
/// <summary>A supplied Layout page-flow contract fixture; the editor performs no placement.</summary>
public sealed record DocumentPageFlow(IReadOnlyList<DocumentFlowBlock> Blocks);
/// <summary>Resolved semantic page content supplied by the host.</summary>
public sealed record DocumentFlowBlock(string Id, string PageVariant, string Content);
/// <summary>A record or explicitly labelled sample; neither choice issues a document.</summary>
public sealed record DocumentPreviewTarget(string Kind, string Id);
/// <summary>The only preview request; no issuance operation is exposed.</summary>
public sealed record DocumentPreviewRequest(string Mode, DocumentDraft Draft, DocumentPageFlow PageFlow, DocumentPreviewTarget Target);
/// <summary>Server output. A result that claims to have minted is refused by the editor.</summary>
public sealed record DocumentPreviewResult(bool Minted, string LocaleTag, IReadOnlyList<DocumentPreviewBlock> Blocks);
/// <summary>One escaped semantic output block.</summary>
public sealed record DocumentPreviewBlock(string Id, string Content);
/// <summary>One server denial, rendered verbatim.</summary>
public sealed record DocumentDiagnostic(string Code, string Pointer);
/// <summary>Refusals fenced to the identity, revision and local generation they admitted.</summary>
public sealed record DocumentDenial(string Identity, string? Revision, int Generation, IReadOnlyList<DocumentDiagnostic> Diagnostics);
/// <summary>A local draft change, not a persisted revision.</summary>
public sealed record DocumentDraftChange(DocumentDraft Draft, int Generation);
