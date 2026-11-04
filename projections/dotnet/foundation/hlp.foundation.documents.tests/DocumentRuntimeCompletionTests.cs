using System.Text;

using Harborline.Blocks.BuilderDefinitions;

using Xunit;

using static Harborline.Foundation.Documents.Tests.TemplateFixtures;

namespace Harborline.Foundation.Documents.Tests;

/// <summary>T-594/runtime-completion. Expected values are literals in the en-US and de-DE patterns.</summary>
public sealed class DocumentRuntimeCompletionTests
{
    private const string PinnedLogo = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly DocumentRenderContext EnUsContext = new("en-US", "en-US", "USD");
    private static readonly DocumentRenderContext DeDeContext = new("de-DE", "en-US", "EUR");

    private static TemplateDefinition FromRecord() => Template() with { Locale = new(TemplateLocaleKind.FromRecord) };

    private static DocumentRuntime Runtime(TemplateDefinition template) => new(new Templates(TemplateDefinitionJson.SerializeCanonical(template)), Surfaces());

    private static DocumentPageFlow Flow(params DocumentFlowRun[] runs) => new([new("line", "first", "ignored", runs)]);

    private static async Task<string> Rendered(DocumentRenderContext context, params DocumentFlowRun[] runs)
        => (await Runtime(FromRecord()).PreviewAsync(new DocumentInlineTemplate(TemplateDefinitionJson.SerializeCanonical(FromRecord())), Flow(runs), context, null)).Document.Blocks.Single().Content;

    [Fact(DisplayName = "documents-eng-3: two blocks render in the page flow's declared order, whatever their ids sort to")]
    public async Task BlocksRenderInDeclaredOrder()
    {
        var flow = new DocumentPageFlow([new("zeta", "first", "first block"), new("alpha", "left", "second block")]);

        var preview = await Runtime(Template()).PreviewAsync(new DocumentTemplateReference("template.invoice", "1.0.0"), flow, EnUsContext, null);

        Assert.Equal([new RenderedDocumentBlock("zeta", "first", "first block"), new RenderedDocumentBlock("alpha", "left", "second block")], preview.Document.Blocks);
    }

    [Fact(DisplayName = "documents-eng-3: a stored placement the pinned surface refuses diagnoses at render rather than normalizing")]
    public async Task InvalidStoredPlacementDiagnoses()
    {
        var bytes = TemplateDefinitionJson.SerializeCanonical(Template());
        var refusing = new TemplateSurfaces(_ => null, (_, _) => [], (_, _) => null, PlatformPackageSeed.ContractWindow);
        var runtime = new DocumentRuntime(new Templates(bytes), refusing);

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await runtime.PreviewAsync(new DocumentInlineTemplate(bytes), new DocumentPageFlow([]), EnUsContext, null));

        Assert.Equal(DefinitionAdmissionPhase.Render, refused.Stage);
    }

    [Fact(DisplayName = "documents-eng-7: a recipient locale renders one value identically under two UI locales")]
    public async Task UiLocaleNeverChoosesTheFormat()
    {
        var template = FromRecord();
        var input = new DocumentInlineTemplate(TemplateDefinitionJson.SerializeCanonical(template));
        var flow = Flow(new DocumentMergeRun("1234.5", DocumentMergeFormat.Decimal));

        var underEnglish = await Runtime(template).PreviewAsync(input, flow, DeDeContext, "en-US");
        var underJapanese = await Runtime(template).PreviewAsync(input, flow, DeDeContext, "ja-JP");

        Assert.Equal("1.234,5", underEnglish.Document.Blocks.Single().Content);
        Assert.Equal("1.234,5", underJapanese.Document.Blocks.Single().Content);
    }

    [Theory(DisplayName = "documents-eng-7: a canonical value renders differently under two document locales, never in raw canonical form")]
    [InlineData(DocumentMergeFormat.Currency, "1234.5", "$1,234.50", "1.234,50 €")]
    [InlineData(DocumentMergeFormat.Date, "2026-03-04", "3/4/2026", "04.03.2026")]
    [InlineData(DocumentMergeFormat.Date, "2026-03-04T10:15:00Z", "3/4/2026", "04.03.2026")]
    [InlineData(DocumentMergeFormat.Integer, "1234", "1,234", "1.234")]
    [InlineData(DocumentMergeFormat.Decimal, "1234.5", "1,234.5", "1.234,5")]
    [InlineData(DocumentMergeFormat.Decimal, "1234.50", "1,234.50", "1.234,50")]
    [InlineData(DocumentMergeFormat.Decimal, "1234", "1,234", "1.234")]
    public async Task EachFormatFollowsTheDocumentLocale(DocumentMergeFormat format, string canonical, string enUs, string deDe)
    {
        Assert.Equal(enUs, await Rendered(EnUsContext, new DocumentMergeRun(canonical, format)));
        Assert.Equal(deDe, await Rendered(DeDeContext, new DocumentMergeRun(canonical, format)));
    }

    [Fact(DisplayName = "documents-eng-7: a text value renders verbatim, and literal and merge runs keep their authored order")]
    public async Task RunsKeepAuthoredOrder()
        => Assert.Equal("Total: 1,234 units of 1.5", await Rendered(EnUsContext,
            new DocumentLiteralRun("Total: "), new DocumentMergeRun("1234", DocumentMergeFormat.Integer),
            new DocumentLiteralRun(" units of "), new DocumentMergeRun("1.5", DocumentMergeFormat.Text)));

    [Theory(DisplayName = "documents-eng-7: currency symbols come from the render context's code, and an unknown code prints itself")]
    [InlineData("GBP", "£10.00")]
    [InlineData("JPY", "¥10.00")]
    [InlineData("CAD", "CA$10.00")]
    [InlineData("AUD", "A$10.00")]
    [InlineData("usd", "$10.00")]
    [InlineData("CHF", "CHF10.00")]
    public async Task CurrencySymbolFollowsTheContext(string code, string expected)
        => Assert.Equal(expected, await Rendered(new("en-US", "en-US", code), new DocumentMergeRun("10", DocumentMergeFormat.Currency)));

    [Fact(DisplayName = "documents-eng-7: an unknown locale tag formats as en-US, never as a culture the host fabricates")]
    public async Task UnknownLocaleFallsBackToEnUs()
    {
        Assert.Equal("$10.00", await Rendered(new("zz-ZZ", "en-US", "USD"), new DocumentMergeRun("10", DocumentMergeFormat.Currency)));
        Assert.Equal("3/4/2026", await Rendered(new("zz-ZZ", "en-US", "USD"), new DocumentMergeRun("2026-03-04", DocumentMergeFormat.Date)));
    }

    [Theory(DisplayName = "documents-eng-7: a malformed canonical value is refused rather than printed raw")]
    [InlineData(DocumentMergeFormat.Currency, "ten")]
    [InlineData(DocumentMergeFormat.Integer, "ten")]
    [InlineData(DocumentMergeFormat.Decimal, "ten")]
    [InlineData(DocumentMergeFormat.Date, "not a date")]
    public async Task MalformedCanonicalValueIsRefused(DocumentMergeFormat format, string value)
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Rendered(EnUsContext, new DocumentMergeRun(value, format)));
        Assert.Equal("documents.merge.value_malformed", refused.Message);
    }

    [Fact(DisplayName = "documents-eng-7: an undefined merge format is refused")]
    public async Task UndefinedFormatIsRefused()
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Rendered(EnUsContext, new DocumentMergeRun("1", (DocumentMergeFormat)99)));
        Assert.Equal("documents.merge.format_unsupported", refused.Message);
    }

    [Fact(DisplayName = "documents-eng-7: an unknown run kind is refused")]
    public async Task UnknownRunKindIsRefused()
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Rendered(EnUsContext, new UnknownRun()));
        Assert.Equal("documents.merge.run_unsupported", refused.Message);
    }

    [Fact(DisplayName = "documents-auth-10: a null merge renders the authored literal fallback, or nothing when none is authored")]
    public async Task NullMergeUsesTheFallback()
    {
        Assert.Equal("n/a", await Rendered(EnUsContext, new DocumentMergeRun(null, DocumentMergeFormat.Currency, "n/a")));
        Assert.Equal("", await Rendered(EnUsContext, new DocumentMergeRun(null, DocumentMergeFormat.Currency)));
    }

    [Fact(DisplayName = "documents-eng-9, documents-bound-1: the writer is chosen by format through a named, parameterised binding")]
    public void WriterIsChosenByFormat()
    {
        var pdf = new DocumentOutputWriterBinding("pdfsharp", new Utf8Writer("pdf"), new Dictionary<string, string> { ["paper"] = "a4" });
        var html = new DocumentOutputWriterBinding("html", new Utf8Writer("html"));
        var writers = new DocumentOutputWriters([pdf, html]);

        Assert.Same(pdf, writers.Resolve("pdf"));
        Assert.Same(pdf, writers.Resolve("PDF"));
        Assert.Same(html, writers.Resolve("html"));
        Assert.Equal("pdfsharp", writers.Resolve("pdf").Name);
        Assert.Equal("a4", writers.Resolve("pdf").Parameters!["paper"]);
    }

    [Fact(DisplayName = "documents-eng-9: an unregistered format and a second writer for one format are refused")]
    public void WriterRegistryRefuses()
    {
        var writers = new DocumentOutputWriters([new("html", new Utf8Writer("html"))]);

        Assert.Equal("documents.writer.format_unknown", Assert.Throws<InvalidOperationException>(() => writers.Resolve("pdf")).Message);
        Assert.Equal("documents.writer.format_duplicate", Assert.Throws<InvalidOperationException>(() =>
            new DocumentOutputWriters([new("first", new Utf8Writer("pdf")), new("second", new Utf8Writer("PDF"))])).Message);
        Assert.Throws<ArgumentNullException>(() => new DocumentOutputWriters(null!));
    }

    [Fact(DisplayName = "documents-eng-9: two different writer adapters for one format carry the same semantic output")]
    public async Task TwoAdaptersPreserveSemantics()
    {
        var document = (await Runtime(Template()).PreviewAsync(new DocumentTemplateReference("template.invoice", "1.0.0"),
            new DocumentPageFlow([new("a", "first", "one"), new("b", "left", "two")]), EnUsContext, null)).Document;

        var plain = Encoding.UTF8.GetString((await new Utf8Writer("pdf").WriteAsync(document)).Span);
        var framed = Encoding.UTF8.GetString((await new FramedWriter().WriteAsync(document)).Span);

        Assert.NotEqual(plain, framed);
        Assert.Equal(plain, framed.Replace("[", "").Replace("]", ""));
    }

    [Fact(DisplayName = "documents-eng-10, documents-run-2: issuance renders through the preview's path, and only a preview is non-minting")]
    public async Task IssuanceAndPreviewShareOneRender()
    {
        var template = Template() with { Style = new("Harborline", PinnedLogo) };
        var reference = new DocumentTemplateReference("template.invoice", "1.0.0");
        var flow = new DocumentPageFlow([new("line", "first", "ignored", [new DocumentMergeRun("1234", DocumentMergeFormat.Integer)])]);

        var preview = await Runtime(template).PreviewAsync(reference, flow, EnUsContext, null);
        var issuance = await Runtime(template).RenderForIssuanceAsync(reference, flow, EnUsContext);

        Assert.Equal(DocumentContentHash.Of(preview.Document), issuance.ContentHash);
        Assert.Equal(preview.Document.Blocks, issuance.Document.Blocks);
        Assert.Equal("1,234", issuance.Document.Blocks.Single().Content);
        Assert.False(preview.Minted);
    }

    [Fact(DisplayName = "documents-eng-10: issuance is refused for what preview refuses, so neither path is laxer")]
    public async Task IssuanceRefusesAnUnpinnedLogo()
    {
        var template = Template() with { Style = new("Harborline", "https://assets.example/logo.svg") };

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Runtime(template).RenderForIssuanceAsync(new DocumentTemplateReference("template.invoice", "1.0.0"), new DocumentPageFlow([]), EnUsContext));

        Assert.Equal("documents.style.logo_not_pinned", refused.Message);
    }

    [Fact(DisplayName = "documents-run-1: the semantic hash is stable and matches a literal computed outside this code")]
    public void HashIsStable()
    {
        var document = Document();

        Assert.Equal(DocumentContentHash.Of(document), DocumentContentHash.Of(Document()));
        // The expected value was computed outside this code: SHA-256 of the compact JSON array of the semantic fields.
        Assert.Equal("sha256:0e682231d9deb2ac818fa77d65a6003ec8e9ce473e60c88c86dc964f0bd7dd74", DocumentContentHash.Of(document));
        Assert.Throws<ArgumentNullException>(() => DocumentContentHash.Of(null!));
    }

    public static TheoryData<string, RenderedDocument> HashChanges => new()
    {
        { "type", Document() with { DocumentType = "receipt" } },
        { "locale", Document() with { LocaleTag = "de-DE" } },
        { "currency", Document() with { CurrencyCode = "EUR" } },
        { "brand", Document() with { Style = new("Other", PinnedLogo) } },
        { "logo", Document() with { Style = new("Harborline", null) } },
        { "no style", Document() with { Style = null } },
        { "block id", Document() with { Blocks = [new("other", "first", "one"), new("b", "left", "two")] } },
        { "block variant", Document() with { Blocks = [new("a", "right", "one"), new("b", "left", "two")] } },
        { "block content", Document() with { Blocks = [new("a", "first", "changed"), new("b", "left", "two")] } },
        { "block order", Document() with { Blocks = [new("b", "left", "two"), new("a", "first", "one")] } },
        { "field boundary", Document() with { Blocks = [new("a", "first", "onea"), new("b", "left", "two")] } },
    };

    [Theory(DisplayName = "documents-run-1: the semantic hash changes when any of type, locale, currency, style or a resolved block changes")]
    [MemberData(nameof(HashChanges))]
    public void HashCoversEverySemanticField(string change, RenderedDocument changed)
    {
        _ = change;
        Assert.NotEqual(DocumentContentHash.Of(Document()), DocumentContentHash.Of(changed));
    }

    [Fact(DisplayName = "documents-run-1: moving text across a block boundary changes the hash")]
    public void HashSeparatesFields()
        => Assert.NotEqual(
            DocumentContentHash.Of(Document() with { Blocks = [new("a", "first", "ab"), new("c", "left", "d")] }),
            DocumentContentHash.Of(Document() with { Blocks = [new("a", "first", "a"), new("bc", "left", "d")] }));

    [Theory(DisplayName = "documents-bound-8: a logo reference that is not digest-pinned is refused at the preview boundary")]
    [InlineData("https://assets.example/logo.svg")]
    [InlineData("/etc/passwd")]
    [InlineData("../logo.svg")]
    [InlineData("sha256:")]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")]
    [InlineData("sha256:0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("sha256:g123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("sha512:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("xsha256:123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public async Task UnpinnedLogoIsRefused(string reference)
    {
        var template = Template() with { Style = new("Harborline", reference) };

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Runtime(template).PreviewAsync(new DocumentTemplateReference("template.invoice", "1.0.0"), new DocumentPageFlow([]), EnUsContext, null));

        Assert.Equal("documents.style.logo_not_pinned", refused.Message);
    }

    [Theory(DisplayName = "documents-bound-8: a digest-pinned logo and no logo both pass")]
    [InlineData(PinnedLogo)]
    [InlineData(null)]
    public async Task PinnedLogoPasses(string? reference)
    {
        var template = Template() with { Style = new("Harborline", reference) };

        var preview = await Runtime(template).PreviewAsync(new DocumentTemplateReference("template.invoice", "1.0.0"), new DocumentPageFlow([]), EnUsContext, null);

        Assert.Equal(reference, preview.Document.Style!.LogoAssetRef);
    }

    private static RenderedDocument Document() => new("invoice", "en-US", "USD", new("Harborline", PinnedLogo),
        [new("a", "first", "one"), new("b", "left", "two")]);

    private sealed record UnknownRun : DocumentFlowRun;

    private sealed class Templates(byte[] body) : IDocumentTemplateSource
    {
        public ValueTask<ReadOnlyMemory<byte>?> ResolvePublishedAsync(string key, string? version, CancellationToken cancellationToken = default)
            => new((ReadOnlyMemory<byte>?)body);
    }

    private sealed class Utf8Writer(string format) : IDocumentOutputWriter
    {
        public string Format => format;

        public ValueTask<ReadOnlyMemory<byte>> WriteAsync(RenderedDocument document, CancellationToken cancellationToken = default)
            => new((ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(string.Join("\n", document.Blocks.Select(block => block.Content))));
    }

    // A different library's output for one format: different bytes, the same semantic content in the same order.
    private sealed class FramedWriter : IDocumentOutputWriter
    {
        public string Format => "pdf";

        public ValueTask<ReadOnlyMemory<byte>> WriteAsync(RenderedDocument document, CancellationToken cancellationToken = default)
            => new((ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(string.Join("\n", document.Blocks.Select(block => $"[{block.Content}]"))));
    }
}
