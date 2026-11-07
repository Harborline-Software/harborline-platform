using System.Text;
using System.Text.Json.Nodes;

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

    [Fact(DisplayName = "documents-eng-3: an invalid stored placement diagnoses at render by name, rather than normalizing")]
    public async Task InvalidStoredPlacementDiagnoses()
    {
        var range = LayoutDefinitionSchema.Numeric(LayoutNumericMember.Span);
        var stored = JsonNode.Parse(LayoutDefinitionJson.SerializeCanonical(Surface()))!;
        stored["blocks"]![0]!["placement"] = new JsonObject { ["span"] = range.Maximum + 5 };
        var bytes = TemplateDefinitionJson.SerializeCanonical(Template());
        var runtime = new DocumentRuntime(new Templates(bytes), Surfaces(stored.ToJsonString()));

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await runtime.PreviewAsync(new DocumentInlineTemplate(bytes), new DocumentPageFlow([new("masthead", "first", "x")]), EnUsContext, null));

        Assert.Equal(DefinitionAdmissionPhase.Render, refused.Stage);
        Assert.Equal([new DefinitionRefusal(LayoutDefinitionCodes.NumericOutOfRange, "/surface/blocks/0/placement/span")], refused.Refusals);
    }

    [Theory(DisplayName = "documents-eng-3: a page variant Layout does not produce is refused by name")]
    [InlineData("")]
    [InlineData("First")]
    [InlineData("second")]
    public async Task UnknownPageVariantIsRefused(string variant)
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Runtime(Template()).PreviewAsync(new DocumentTemplateReference("template.invoice", "1.0.0"), new DocumentPageFlow([new("a", variant, "x")]), EnUsContext, null));

        Assert.Equal("documents.flow.page_variant_invalid", refused.Message);
    }

    [Theory(DisplayName = "documents-eng-3: Layout's three page variants pass")]
    [InlineData("first")]
    [InlineData("left")]
    [InlineData("right")]
    public async Task KnownPageVariantsPass(string variant)
    {
        var preview = await Runtime(Template()).PreviewAsync(new DocumentTemplateReference("template.invoice", "1.0.0"), new DocumentPageFlow([new("a", variant, "x")]), EnUsContext, null);

        Assert.Equal(variant, preview.Document.Blocks.Single().PageVariant);
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
    [InlineData(DocumentMergeFormat.Date, "2026-03-04T23:30:00.5-05:00", "3/4/2026", "04.03.2026")]
    [InlineData(DocumentMergeFormat.Decimal, "-1234.5", "-1,234.5", "-1.234,5")]
    [InlineData(DocumentMergeFormat.Integer, "1234.0", "1,234", "1.234")]
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

    [Theory(DisplayName = "documents-eng-7: currency symbol and digits come from the render context's code (ISO 4217), and an unknown code prints itself")]
    [InlineData("GBP", "10", "£10.00")]
    [InlineData("JPY", "10", "¥10")]
    [InlineData("CAD", "10", "CA$10.00")]
    [InlineData("AUD", "10", "A$10.00")]
    [InlineData("usd", "10", "$10.00")]
    [InlineData("CHF", "10", "CHF10.00")]
    [InlineData("KWD", "10.125", "KWD10.125")]
    [InlineData("CLF", "10.1255", "CLF10.1255")]
    public async Task CurrencyFollowsTheCode(string code, string value, string expected)
        => Assert.Equal(expected, await Rendered(new("en-US", "en-US", code), new DocumentMergeRun(value, DocumentMergeFormat.Currency)));

    [Theory(DisplayName = "documents-eng-7: every ISO 4217 currency with other than two minor units prints its own digits")]
    [InlineData(0, "BIF", "CLP", "DJF", "GNF", "ISK", "KMF", "KRW", "PYG", "RWF", "UGX", "UYI", "VND", "VUV", "XAF", "XOF", "XPF")]
    [InlineData(3, "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND")]
    [InlineData(4, "CLF", "UYW")]
    public async Task MinorUnitsFollowIso4217(int digits, params string[] codes)
    {
        var fraction = digits == 0 ? "" : "." + new string('0', digits);
        foreach (var code in codes)
            Assert.Equal(code + "10" + fraction, await Rendered(new("en-US", "en-US", code), new DocumentMergeRun("10", DocumentMergeFormat.Currency)));
    }

    [Fact(DisplayName = "documents-eng-7: a culture's own currency digits never drop an amount's minor units")]
    public async Task CultureDoesNotChooseCurrencyDigits()
        => Assert.Equal("$1,234.56", await Rendered(new("ja-JP", "en-US", "USD"), new DocumentMergeRun("1234.56", DocumentMergeFormat.Currency)));

    [Theory(DisplayName = "documents-eng-7: an amount finer than the currency's minor unit is refused rather than rounded")]
    [InlineData("USD", "1234.567")]
    [InlineData("JPY", "10.5")]
    public async Task SubMinorAmountIsRefused(string code, string value)
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Rendered(new("en-US", "en-US", code), new DocumentMergeRun(value, DocumentMergeFormat.Currency)));
        Assert.Equal("documents.merge.value_malformed", refused.Message);
    }

    [Theory(DisplayName = "documents-eng-7: a currency run needs a three-letter currency code; other runs do not")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("US1")]
    [InlineData("USDX")]
    public async Task CurrencyRunNeedsAValidCode(string? code)
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Rendered(new("en-US", "en-US", code!), new DocumentMergeRun("10", DocumentMergeFormat.Currency)));
        Assert.Equal("documents.merge.currency_invalid", refused.Message);
        Assert.Equal("10", await Rendered(new("en-US", "en-US", code!), new DocumentMergeRun("10", DocumentMergeFormat.Text)));
    }

    [Fact(DisplayName = "documents-eng-7: an unknown locale tag formats as en-US, never as a culture the host fabricates")]
    public async Task UnknownLocaleFallsBackToEnUs()
    {
        Assert.Equal("$10.00", await Rendered(new("zz-ZZ", "en-US", "USD"), new DocumentMergeRun("10", DocumentMergeFormat.Currency)));
        Assert.Equal("3/4/2026", await Rendered(new("zz-ZZ", "en-US", "USD"), new DocumentMergeRun("2026-03-04", DocumentMergeFormat.Date)));
    }

    [Theory(DisplayName = "documents-eng-7: a negative amount keeps its sign under each locale's own pattern")]
    [InlineData("en-US", "USD", "-$1,234.50")]
    [InlineData("de-DE", "EUR", "-1.234,50 €")]
    [InlineData("en-GB", "GBP", "-£1,234.50")]
    public async Task NegativeCurrencyKeepsItsSign(string locale, string code, string expected)
        => Assert.Equal(expected, await Rendered(new(locale, "en-US", code), new DocumentMergeRun("-1234.5", DocumentMergeFormat.Currency)));

    [Theory(DisplayName = "documents-eng-7: a locale tag that is not language, script and region is refused rather than half-parsed")]
    [InlineData("de_DE")]
    [InlineData("de-DE-u-nu-arab")]
    [InlineData("ar-SA-u-ca-gregory")]
    [InlineData("en-US-x-private")]
    [InlineData("de-")]
    [InlineData("d")]
    [InlineData("de-DE\n")]
    [InlineData("ca-ES-valencia")]
    [InlineData("de-DE-1996")]
    [InlineData("en-US-posix")]
    [InlineData("es-٤١٩")]
    public async Task MalformedLocaleTagIsRefused(string tag)
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Rendered(new(tag, "en-US", "USD"), new DocumentLiteralRun("x")));
        Assert.Equal("documents.template.locale_invalid", refused.Message);
    }

    [Theory(DisplayName = "documents-eng-7: script and numeric-region tags pass the shape check")]
    [InlineData("zh-Hant-TW", "zh-Hant-TW")]
    [InlineData("es-419", "es-419")]
    [InlineData("sr-Latn", "sr-Latn")]
    [InlineData("und", "en-US")]
    public async Task WellFormedLocaleTagsPass(string tag, string name)
    {
        var template = FromRecord();
        var preview = await Runtime(template).PreviewAsync(
            new DocumentInlineTemplate(TemplateDefinitionJson.SerializeCanonical(template)),
            new DocumentPageFlow([new("a", "first", "x")]), new(tag, "en-US", "USD"), null);
        Assert.Equal(name, preview.Document.LocaleTag);
    }

    [Theory(DisplayName = "documents-eng-7: publish refuses a fixed locale tag that could never render")]
    [InlineData("de_DE")]
    [InlineData("de-DE-u-nu-arab")]
    [InlineData("ca-ES-valencia")]
    public void PublishRefusesAMalformedFixedLocale(string tag)
    {
        var template = Template() with { Locale = new(TemplateLocaleKind.Fixed, tag) };

        Assert.Equal([new DefinitionRefusal(TemplateDefinitionCodes.LocaleInvalid, "/locale")], TemplateDefinitionAdmission.Validate(template, Surfaces()));
        Assert.Throws<DefinitionRefusalException>(() => TemplatePack.Export(template, Surfaces()));
    }

    [Theory(DisplayName = "documents-eng-7: a date outside the culture's calendar is refused by name, never an unnamed exception")]
    [InlineData("0001-01-01")]
    [InlineData("0001-01-01T00:00:00Z")]
    public async Task DateOutsideTheCalendarIsRefused(string value)
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Rendered(new("fa-IR", "en-US", "IRR"), new DocumentMergeRun(value, DocumentMergeFormat.Date)));
        Assert.Equal("documents.merge.date_outside_calendar", refused.Message);
    }

    [Theory(DisplayName = "documents-eng-7: a malformed canonical value is refused rather than printed raw")]
    [InlineData(DocumentMergeFormat.Currency, "ten")]
    [InlineData(DocumentMergeFormat.Integer, "ten")]
    [InlineData(DocumentMergeFormat.Decimal, "ten")]
    [InlineData(DocumentMergeFormat.Date, "not a date")]
    [InlineData(DocumentMergeFormat.Decimal, "1,5")]
    [InlineData(DocumentMergeFormat.Decimal, "1,2,3")]
    [InlineData(DocumentMergeFormat.Decimal, "12-")]
    [InlineData(DocumentMergeFormat.Decimal, "+12")]
    [InlineData(DocumentMergeFormat.Decimal, " 12")]
    [InlineData(DocumentMergeFormat.Decimal, ".5")]
    [InlineData(DocumentMergeFormat.Decimal, "5.")]
    [InlineData(DocumentMergeFormat.Decimal, "-")]
    [InlineData(DocumentMergeFormat.Decimal, "")]
    [InlineData(DocumentMergeFormat.Decimal, "99999999999999999999999999999999")]
    [InlineData(DocumentMergeFormat.Integer, "1234.5")]
    [InlineData(DocumentMergeFormat.Date, "10:15")]
    [InlineData(DocumentMergeFormat.Date, "3/4/2026")]
    [InlineData(DocumentMergeFormat.Date, "2026-03-04T10:15:00")]
    [InlineData(DocumentMergeFormat.Date, "2026-03-04T10:15:00.Z")]
    [InlineData(DocumentMergeFormat.Date, "2026-03-04T10:15:00-0500")]
    [InlineData(DocumentMergeFormat.Date, "2026-03-04T10:15:00-5:00")]
    [InlineData(DocumentMergeFormat.Date, "2026-13-04")]
    [InlineData(DocumentMergeFormat.Date, "2026-03-04T25:15:00Z")]
    [InlineData(DocumentMergeFormat.Date, "2026-03-04T24:00:00Z")]
    [InlineData(DocumentMergeFormat.Decimal, "1.00000000000000000000000000000001")]
    [InlineData(DocumentMergeFormat.Integer, "1.00000000000000000000000000000001")]
    [InlineData(DocumentMergeFormat.Currency, "10.00000000000000000000000000000049")]
    [InlineData(DocumentMergeFormat.Decimal, "007")]
    [InlineData(DocumentMergeFormat.Decimal, "-0")]
    [InlineData(DocumentMergeFormat.Currency, "-0.00")]
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

        Assert.Equal("one\ntwo", plain);
        Assert.Equal("[one]\n[two]", framed);
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
        Assert.Equal("template.invoice", issuance.TemplateKey);
        Assert.Equal("1.0.0", issuance.TemplateVersion);
    }

    [Fact(DisplayName = "documents-eng-10: issuance is refused for what preview refuses, so neither path is laxer")]
    public async Task IssuanceRefusesAnUnpinnedLogo()
    {
        var template = Template() with { Style = new("Harborline", "https://assets.example/logo.svg") };

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await Runtime(template).RenderForIssuanceAsync(new DocumentTemplateReference("template.invoice", "1.0.0"), new DocumentPageFlow([]), EnUsContext));

        Assert.Equal([new DefinitionRefusal(TemplateDefinitionCodes.LogoNotPinned, "/style/logo_asset_ref")], refused.Refusals);
    }

    [Theory(DisplayName = "documents-eng-10: issuance needs an exact version, because the issued record pins it")]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task IssuanceNeedsAnExactVersion(string? version)
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Runtime(Template()).RenderForIssuanceAsync(new DocumentTemplateReference("template.invoice", version), new DocumentPageFlow([]), EnUsContext));

        Assert.Equal("documents.template.version_required", refused.Message);
    }

    [Fact(DisplayName = "documents-eng-10: issuance refuses a null reference")]
    public async Task IssuanceRefusesANullReference()
        => await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await Runtime(Template()).RenderForIssuanceAsync(null!, new DocumentPageFlow([]), EnUsContext));

    [Theory(DisplayName = "documents-eng-1: a template source answering a pin with another template is refused")]
    [InlineData("template.other", "1.0.0")]
    [InlineData("template.invoice", "2.0.0")]
    public async Task ReferenceMustMatchTheBody(string key, string version)
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Runtime(Template()).PreviewAsync(new DocumentTemplateReference(key, version), new DocumentPageFlow([]), EnUsContext, null));

        Assert.Equal("documents.template.reference_mismatch", refused.Message);
    }

    [Fact(DisplayName = "documents-eng-1: a preview reference with no version matches the body's identity")]
    public async Task PreviewReferenceWithoutAVersionPasses()
    {
        var preview = await Runtime(Template()).PreviewAsync(new DocumentTemplateReference("template.invoice", null), new DocumentPageFlow([]), EnUsContext, null);

        Assert.Equal("invoice", preview.Document.DocumentType);
    }

    [Fact(DisplayName = "documents-run-1: the semantic hash is stable and matches a literal computed outside this code")]
    public void HashIsStable()
    {
        var document = Document();

        Assert.Equal(DocumentContentHash.Of(document), DocumentContentHash.Of(Document()));
        // The expected value was computed outside this code, from the encoding interface.yaml specifies.
        Assert.Equal("sha256:caa445cbfa44e265c3e41436a64a94a5790ac561ada0c68dfc33f0ed43be84b9", DocumentContentHash.Of(document));
        Assert.Throws<ArgumentNullException>(() => DocumentContentHash.Of(null!));
    }

    [Fact(DisplayName = "documents-run-1: any text hashes as its UTF-8 bytes, so a French amount's U+202F grouping and an emoji match a literal computed outside this code")]
    public void HashEncodesEveryCharacterAsUtf8()
    {
        var document = new RenderedDocument("invoice", "fr-FR", "EUR", new("Müller & Söhne \"SA\"", PinnedLogo),
            [new("a", "first", "1\u202f234,50\u00a0€ <b> 😀\u2028"), new("b", "left", "two\n")]);

        Assert.Equal("sha256:584e4a6682dd8bce38e192e936272f7e78733a1e5f257b5495864766be6334ef", DocumentContentHash.Of(document));
    }

    [Fact(DisplayName = "documents-run-1: an absent style encodes as the specified null fields, matching a literal computed outside this code")]
    public void HashEncodesAbsentFields()
        => Assert.Equal(
            "sha256:ef4358e5a0e082e71d3e1c3749e29797b4faf6f1d68e1575e2ddde14b04ce2de",
            DocumentContentHash.Of(new RenderedDocument("invoice", "en-US", "USD", null, [])));

    [Theory(DisplayName = "documents-run-1: spellings of one currency code report and hash as ISO 4217 upper case")]
    [InlineData("usd")]
    [InlineData("Usd")]
    public async Task CurrencyIsReportedUpperCase(string spelled)
    {
        var template = FromRecord();
        var input = new DocumentInlineTemplate(TemplateDefinitionJson.SerializeCanonical(template));
        var flow = new DocumentPageFlow([new("a", "first", "x")]);

        var preview = await Runtime(template).PreviewAsync(input, flow, new("en-US", "en-US", spelled), null);
        var upper = await Runtime(template).PreviewAsync(input, flow, new("en-US", "en-US", "USD"), null);

        Assert.Equal("USD", preview.Document.CurrencyCode);
        Assert.Equal(DocumentContentHash.Of(upper.Document), DocumentContentHash.Of(preview.Document));
    }

    [Theory(DisplayName = "documents-run-1: a currency code that is not alpha-3 is reported as given, because only a currency run refuses it")]
    [InlineData(null)]
    [InlineData("usdx")]
    [InlineData("us1")]
    public async Task OtherCurrencyIsReportedAsGiven(string? given)
    {
        var template = FromRecord();
        var input = new DocumentInlineTemplate(TemplateDefinitionJson.SerializeCanonical(template));

        var preview = await Runtime(template).PreviewAsync(input, new DocumentPageFlow([new("a", "first", "x")]), new("en-US", "en-US", given!), null);

        Assert.Equal(given, preview.Document.CurrencyCode);
    }

    [Theory(DisplayName = "documents-run-1: spellings of one locale report and hash as the resolved culture, so equal content hashes equally")]
    [InlineData("de-de", "de-DE")]
    [InlineData("DE-DE", "de-DE")]
    [InlineData("zz-ZZ", "en-US")]
    public async Task LocaleIsReportedAsResolved(string spelled, string resolved)
    {
        var template = FromRecord();
        var input = new DocumentInlineTemplate(TemplateDefinitionJson.SerializeCanonical(template));
        var flow = new DocumentPageFlow([new("a", "first", "x")]);

        var spelledPreview = await Runtime(template).PreviewAsync(input, flow, new(spelled, "en-US", "USD"), null);
        var resolvedPreview = await Runtime(template).PreviewAsync(input, flow, new(resolved, "en-US", "USD"), null);

        Assert.Equal(resolved, spelledPreview.Document.LocaleTag);
        Assert.Equal(DocumentContentHash.Of(resolvedPreview.Document), DocumentContentHash.Of(spelledPreview.Document));
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

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await Runtime(template).PreviewAsync(new DocumentTemplateReference("template.invoice", "1.0.0"), new DocumentPageFlow([]), EnUsContext, null));

        Assert.Equal(DefinitionAdmissionPhase.Render, refused.Stage);
        Assert.Equal([new DefinitionRefusal(TemplateDefinitionCodes.LogoNotPinned, "/style/logo_asset_ref")], refused.Refusals);
    }

    [Fact(DisplayName = "documents-bound-8: publish refuses an unpinned logo, so a published template can always render")]
    public void PublishRefusesAnUnpinnedLogo()
    {
        var template = Template() with { Style = new("Harborline", "https://assets.example/logo.svg") };

        Assert.Equal([new DefinitionRefusal(TemplateDefinitionCodes.LogoNotPinned, "/style/logo_asset_ref")], TemplateDefinitionAdmission.Validate(template, Surfaces()));
        Assert.Throws<DefinitionRefusalException>(() => TemplatePack.Export(template, Surfaces()));
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
