using System.Text.Json;

namespace Harborline.Foundation.Documents;

/// <summary>Names the immutable template source a render request uses.</summary>
public abstract record DocumentTemplateInput;

/// <summary>Requests an exact published template revision.</summary>
/// <param name="Key">The stable template identifier.</param>
/// <param name="Version">The optional immutable version requested by the caller.</param>
public sealed record DocumentTemplateReference(string Key, string? Version) : DocumentTemplateInput;

/// <summary>Supplies an unpublished template body for a non-minting preview.</summary>
/// <param name="CanonicalJson">The template's canonical JSON contract.</param>
public sealed record DocumentInlineTemplate(ReadOnlyMemory<byte> CanonicalJson) : DocumentTemplateInput;

/// <summary>Resolves published template bytes without choosing a current revision on the caller's behalf.</summary>
public interface IDocumentTemplateSource
{
    /// <summary>Returns the canonical body for one requested published template, or no value when it cannot resolve.</summary>
    ValueTask<ReadOnlyMemory<byte>?> ResolvePublishedAsync(
        string key,
        string? version,
        CancellationToken cancellationToken = default);
}

/// <summary>One Layout-produced block in page-flow order, represented without a blocks-tier dependency.</summary>
/// <param name="Id">The definition-local Layout block identifier.</param>
/// <param name="PageVariant">The Layout page variant that carries the block.</param>
/// <param name="Content">The already-resolved semantic content.</param>
public sealed record DocumentFlowBlock(string Id, string PageVariant, string Content);

/// <summary>Layout's page-flow output passed across the foundation-tier boundary.</summary>
/// <param name="Blocks">The page-flow blocks in Layout's declared order.</param>
public sealed record DocumentPageFlow(IReadOnlyList<DocumentFlowBlock> Blocks);

/// <summary>Provides the locale and currency values that belong to one render, never the editor UI locale.</summary>
/// <param name="RecipientLocale">The locale carried on the recipient record.</param>
/// <param name="InstanceLocale">The instance's configured default locale.</param>
/// <param name="CurrencyCode">The currency code attached to the rendering context.</param>
public sealed record DocumentRenderContext(string? RecipientLocale, string InstanceLocale, string CurrencyCode);

/// <summary>One resolved semantic block of a rendered document.</summary>
/// <param name="Id">The Layout block identifier.</param>
/// <param name="PageVariant">The selected first, left, or right Layout page variant.</param>
/// <param name="Content">The resolved block content.</param>
public sealed record RenderedDocumentBlock(string Id, string PageVariant, string Content);

/// <summary>Renderer-neutral Documents output: semantic content, never library-specific bytes.</summary>
/// <param name="DocumentType">The template's issued-document discriminator.</param>
/// <param name="LocaleTag">The locale resolved from the authored document policy.</param>
/// <param name="CurrencyCode">The render context's currency code.</param>
/// <param name="Style">The authored masthead style; it is not product chrome.</param>
/// <param name="Blocks">The Layout page-flow blocks in rendered order.</param>
public sealed record RenderedDocument(
    string DocumentType,
    string LocaleTag,
    string CurrencyCode,
    TemplateStyle? Style,
    IReadOnlyList<RenderedDocumentBlock> Blocks);

/// <summary>One preview result. It carries no issuance identifier and cannot mint an issued record.</summary>
/// <param name="Document">The renderer-neutral semantic output.</param>
public sealed record DocumentPreview(RenderedDocument Document)
{
    /// <summary>Always false because preview is a render-only route.</summary>
    public bool Minted => false;
}

/// <summary>Library-neutral writer boundary; a host selects one adapter by its declared output format.</summary>
public interface IDocumentOutputWriter
{
    /// <summary>The output format this writer adapter represents.</summary>
    string Format { get; }

    /// <summary>Turns semantic output into host-owned bytes without changing its semantic order.</summary>
    ValueTask<ReadOnlyMemory<byte>> WriteAsync(RenderedDocument document, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves a published or inline template through its canonical contract and creates a non-minting semantic
/// preview from Layout-owned page flow. Layout owns traversal and page selection; this seam retains Documents'
/// locale, style, semantic output, and writer boundary.
/// </summary>
public sealed class DocumentRuntime
{
    private readonly IDocumentTemplateSource _templates;
    private readonly TemplateSurfaces _surfaces;

    /// <summary>Creates the Documents runtime over host-supplied template storage and Layout admission.</summary>
    public DocumentRuntime(IDocumentTemplateSource templates, TemplateSurfaces surfaces)
    {
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
    }

    /// <summary>
    /// Creates one non-minting preview. Both published and inline bodies pass through the same canonical parser and
    /// persisted-value admission; <paramref name="authoringUiLocale"/> is deliberately ignored because only the
    /// template's document locale policy selects the render locale.
    /// </summary>
    public async ValueTask<DocumentPreview> PreviewAsync(
        DocumentTemplateInput input,
        DocumentPageFlow pageFlow,
        DocumentRenderContext context,
        string? authoringUiLocale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(pageFlow);
        ArgumentNullException.ThrowIfNull(context);
        _ = authoringUiLocale;

        var body = await ResolveAsync(input, cancellationToken).ConfigureAwait(false);
        var template = TemplateDefinitionJson.Deserialize(body.Span);
        TemplateDefinitionAdmission.ValidatePersisted(template, _surfaces);
        var locale = ResolveLocale(template.Locale, context);
        var blocks = (pageFlow.Blocks ?? [])
            .Select(block => new RenderedDocumentBlock(block.Id, block.PageVariant, block.Content))
            .ToArray();
        return new(new(template.DocumentType, locale, context.CurrencyCode, template.Style, blocks));
    }

    private async ValueTask<ReadOnlyMemory<byte>> ResolveAsync(DocumentTemplateInput input, CancellationToken cancellationToken)
        => input switch
        {
            DocumentInlineTemplate inline => inline.CanonicalJson,
            DocumentTemplateReference reference => await ResolvePublishedAsync(reference, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException("The document template input is unsupported.", nameof(input)),
        };

    private async ValueTask<ReadOnlyMemory<byte>> ResolvePublishedAsync(DocumentTemplateReference reference, CancellationToken cancellationToken)
    {
        var body = await _templates.ResolvePublishedAsync(reference.Key, reference.Version, cancellationToken).ConfigureAwait(false);
        return body ?? throw new InvalidOperationException("documents.template.published_not_found");
    }

    private static string ResolveLocale(TemplateLocalePolicy policy, DocumentRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.Kind switch
        {
            TemplateLocaleKind.Fixed when !string.IsNullOrWhiteSpace(policy.Tag) => policy.Tag,
            TemplateLocaleKind.FromRecord when !string.IsNullOrWhiteSpace(context.RecipientLocale) => context.RecipientLocale,
            TemplateLocaleKind.FromInstance when !string.IsNullOrWhiteSpace(context.InstanceLocale) => context.InstanceLocale,
            _ => throw new InvalidOperationException("documents.template.locale_unresolved"),
        };
    }
}
