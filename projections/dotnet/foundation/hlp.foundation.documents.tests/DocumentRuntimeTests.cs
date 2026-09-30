using System.Text;

using Harborline.Blocks.BuilderDefinitions;

using Xunit;

using static Harborline.Foundation.Documents.Tests.TemplateFixtures;

namespace Harborline.Foundation.Documents.Tests;

/// <summary>Regression coverage for the Documents semantic-render and non-minting preview seam.</summary>
public sealed class DocumentRuntimeTests
{
    [Fact(DisplayName = "documents-eng-1,3,7,8,9,10 and documents-run-1,2: published and inline templates use one parser and one semantic preview path")]
    public async Task PreviewUsesOneSemanticPathForPublishedAndInlineTemplates()
    {
        var template = Template() with
        {
            Locale = new(TemplateLocaleKind.FromRecord),
            Style = new("Harborline", "https://assets.example/logo.svg"),
        };
        var bytes = TemplateDefinitionJson.SerializeCanonical(template);
        var source = new CapturingTemplates(bytes);
        var runtime = new DocumentRuntime(source, Surfaces());
        var flow = new DocumentPageFlow([new("masthead", "first", "Invoice 1,234.50")]);
        var context = new DocumentRenderContext("fr-FR", "en-US", "USD");

        var published = await runtime.PreviewAsync(new DocumentTemplateReference("template.invoice", "1.0.0"), flow, context, "ui-en-US");
        var inline = await runtime.PreviewAsync(new DocumentInlineTemplate(bytes), flow, context, "ui-de-DE");

        Assert.Equal(published.Document.DocumentType, inline.Document.DocumentType);
        Assert.Equal(published.Document.LocaleTag, inline.Document.LocaleTag);
        Assert.Equal(published.Document.CurrencyCode, inline.Document.CurrencyCode);
        Assert.Equal(published.Document.Style, inline.Document.Style);
        Assert.Equal(published.Document.Blocks, inline.Document.Blocks);
        Assert.Equal("fr-FR", published.Document.LocaleTag);
        Assert.Equal("USD", published.Document.CurrencyCode);
        Assert.Equal(["masthead"], published.Document.Blocks.Select(block => block.Id));
        Assert.Equal(1, source.Calls);
        Assert.False(published.Minted);
        Assert.False(inline.Minted);

        var first = new Utf8Writer("pdf");
        var second = new Utf8Writer("pdf");
        Assert.Equal(await first.WriteAsync(published.Document), await second.WriteAsync(published.Document));
    }

    [Fact(DisplayName = "documents-eng-1: an inline draft is re-admitted at the persisted read, so one outside the contract window never renders")]
    public async Task InlineTemplateOutsideTheContractWindowIsRefusedBeforeRender()
    {
        var outside = Template() with { Envelope = Template().Envelope with { Contract = null } };
        var runtime = new DocumentRuntime(new CapturingTemplates([]), Surfaces());

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await runtime.PreviewAsync(
            new DocumentInlineTemplate(TemplateDefinitionJson.SerializeCanonical(outside)),
            new DocumentPageFlow([]), new DocumentRenderContext("fr-FR", "en-US", "USD"), null));

        Assert.Equal(DefinitionAdmissionPhase.Render, refused.Stage);
    }

    [Fact(DisplayName = "documents-eng-1: a published key and version that resolve to nothing refuse instead of rendering an empty document")]
    public async Task MissingPublishedTemplateIsRefused()
    {
        var runtime = new DocumentRuntime(new CapturingTemplates(null), Surfaces());

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.PreviewAsync(
            new DocumentTemplateReference("template.invoice", "9.9.9"),
            new DocumentPageFlow([]), new DocumentRenderContext("fr-FR", "en-US", "USD"), null));

        Assert.Equal("documents.template.published_not_found", refused.Message);
    }

    [Fact(DisplayName = "documents-eng-7: a from-record locale with no recipient locale refuses rather than falling back to the instance or UI locale")]
    public async Task UnresolvedDocumentLocaleIsRefused()
    {
        var template = Template() with { Locale = new(TemplateLocaleKind.FromRecord) };
        var runtime = new DocumentRuntime(new CapturingTemplates([]), Surfaces());

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.PreviewAsync(
            new DocumentInlineTemplate(TemplateDefinitionJson.SerializeCanonical(template)),
            new DocumentPageFlow([]), new DocumentRenderContext(null, "en-US", "USD"), "ui-en-US"));

        Assert.Equal("documents.template.locale_unresolved", refused.Message);
    }

    private sealed class CapturingTemplates(byte[]? body) : IDocumentTemplateSource
    {
        public int Calls { get; private set; }

        public ValueTask<ReadOnlyMemory<byte>?> ResolvePublishedAsync(string key, string? version, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(body is null ? (ReadOnlyMemory<byte>?)null : body);
        }
    }

    private sealed class Utf8Writer(string format) : IDocumentOutputWriter
    {
        public string Format => format;

        public ValueTask<ReadOnlyMemory<byte>> WriteAsync(RenderedDocument document, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ReadOnlyMemory<byte>>(Encoding.UTF8.GetBytes($"{document.DocumentType}|{document.LocaleTag}|{document.Blocks.Count}"));
    }
}
