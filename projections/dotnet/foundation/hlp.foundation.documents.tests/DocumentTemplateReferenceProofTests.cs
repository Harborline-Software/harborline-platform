using Xunit;

using static Harborline.Foundation.Documents.Tests.TemplateFixtures;

namespace Harborline.Foundation.Documents.Tests;

/// <summary>Literal reference-resolution oracles from T-594/template-reference-proof acceptance 1–4.</summary>
public sealed class DocumentTemplateReferenceProofTests
{
    private static readonly DocumentPageFlow Flow = new([
        new("z-block", "first", "first literal"),
        new("a-block", "left", "second literal"),
    ]);
    private static readonly DocumentRenderContext Context = new("en-US", "en-US", "USD");

    [Theory(DisplayName = "documents-eng-1, documents-eng-10, documents-run-2 template-reference: both entry points preserve old/new/old pins and the caller token")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactPinsAndCallerTokenSurviveNewerBody(bool issuance)
    {
        using var caller = new CancellationTokenSource();
        Assert.True(caller.Token.CanBeCanceled);
        Assert.False(caller.IsCancellationRequested);
        var one = AdmittedBody("template.invoice", "1.0.0", "PINNED-ONE");
        var two = AdmittedBody("template.invoice", "2.0.0", "PINNED-TWO");
        var source = new CapturingSource(one);
        var runtime = new DocumentRuntime(source, Surfaces());

        await AssertRender(runtime, issuance, new("template.invoice", "1.0.0"), "1.0.0", "PINNED-ONE", caller.Token);
        AssertCall(source, "template.invoice", "1.0.0", caller.Token);

        source.Body = two;
        await AssertRender(runtime, issuance, new("template.invoice", "2.0.0"), "2.0.0", "PINNED-TWO", caller.Token);
        AssertCall(source, "template.invoice", "2.0.0", caller.Token);

        source.Body = one;
        await AssertRender(runtime, issuance, new("template.invoice", "1.0.0"), "1.0.0", "PINNED-ONE", caller.Token);
        AssertCall(source, "template.invoice", "1.0.0", caller.Token);
    }

    [Theory(DisplayName = "documents-eng-1, documents-eng-10, documents-run-2 template-reference: wrong key, version and ordinal case refuse, then corrected coordinates render")]
    [InlineData(false, "template.invoice", "template.other", "1.0.0")]
    [InlineData(true, "template.invoice", "template.other", "1.0.0")]
    [InlineData(false, "template.invoice", "template.invoice", "2.0.0")]
    [InlineData(true, "template.invoice", "template.invoice", "2.0.0")]
    [InlineData(false, "template.INVOICE", "template.invoice", "1.0.0")]
    [InlineData(true, "template.INVOICE", "template.invoice", "1.0.0")]
    public async Task OtherCoordinatesRefuseBeforeReturningOutput(bool issuance, string requestedKey, string returnedKey, string returnedVersion)
    {
        using var caller = new CancellationTokenSource();
        var source = new CapturingSource(AdmittedBody(returnedKey, returnedVersion, "PINNED-ONE"));
        var runtime = new DocumentRuntime(source, Surfaces());
        var reference = new DocumentTemplateReference(requestedKey, "1.0.0");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (issuance) await runtime.RenderForIssuanceAsync(reference, Flow, Context, caller.Token);
            else await runtime.PreviewAsync(reference, Flow, Context, null, caller.Token);
        });

        Assert.Equal("documents.template.reference_mismatch", refused.Message);
        AssertCall(source, requestedKey, "1.0.0", caller.Token);

        source.Body = AdmittedBody("template.invoice", "1.0.0", "PINNED-ONE");
        await AssertRender(runtime, issuance, new("template.invoice", "1.0.0"), "1.0.0", "PINNED-ONE", caller.Token);
        AssertCall(source, "template.invoice", "1.0.0", caller.Token);
    }

    [Theory(DisplayName = "documents-eng-1, documents-eng-10, documents-run-2 template-reference: missing published bytes refuse distinctly and a matching 9.9.9 body recovers")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingBodyRefusesWithPublishedNotFound(bool issuance)
    {
        using var caller = new CancellationTokenSource();
        var source = new CapturingSource(null);
        var runtime = new DocumentRuntime(source, Surfaces());
        var reference = new DocumentTemplateReference("template.invoice", "9.9.9");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (issuance) await runtime.RenderForIssuanceAsync(reference, Flow, Context, caller.Token);
            else await runtime.PreviewAsync(reference, Flow, Context, null, caller.Token);
        });

        Assert.Equal("documents.template.published_not_found", refused.Message);
        AssertCall(source, "template.invoice", "9.9.9", caller.Token);

        source.Body = AdmittedBody("template.invoice", "9.9.9", "PINNED-ONE");
        await AssertRender(runtime, issuance, reference, "9.9.9", "PINNED-ONE", caller.Token);
        AssertCall(source, "template.invoice", "9.9.9", caller.Token);
    }

    [Fact(DisplayName = "documents-eng-1, documents-eng-10, documents-run-2 template-reference: unversioned preview forwards null but still matches identity")]
    public async Task PreviewForwardsNullVersionAndStillRefusesOtherIdentity()
    {
        using var caller = new CancellationTokenSource();
        var source = new CapturingSource(AdmittedBody("template.invoice", "2.0.0", "PINNED-TWO"));
        var runtime = new DocumentRuntime(source, Surfaces());
        var reference = new DocumentTemplateReference("template.invoice", null);

        var preview = await runtime.PreviewAsync(reference, Flow, Context, null, caller.Token);
        AssertDocument(preview.Document, "PINNED-TWO");
        Assert.False(preview.Minted);
        AssertCall(source, "template.invoice", null, caller.Token);

        source.Body = AdmittedBody("template.other", "2.0.0", "PINNED-TWO");
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.PreviewAsync(reference, Flow, Context, null, caller.Token));
        Assert.Equal("documents.template.reference_mismatch", refused.Message);
        AssertCall(source, "template.invoice", null, caller.Token);
    }

    [Fact(DisplayName = "documents-eng-1, documents-eng-10, documents-run-2 template-reference: inline preview renders full literals without consulting the source or minting")]
    public async Task InlinePreviewNeverResolvesPublishedBytes()
    {
        using var caller = new CancellationTokenSource();
        var source = new CapturingSource(null);
        var runtime = new DocumentRuntime(source, Surfaces());

        var preview = await runtime.PreviewAsync(
            new DocumentInlineTemplate(AdmittedBody("template.invoice", "1.0.0", "PINNED-ONE")), Flow, Context, null, caller.Token);

        AssertDocument(preview.Document, "PINNED-ONE");
        Assert.False(preview.Minted);
        Assert.Empty(source.Calls);
    }

    [Theory(DisplayName = "documents-eng-10, documents-run-2 template-reference: null, empty and whitespace issuance versions refuse before resolution and an explicit version recovers")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task IssuanceRequiresAnExplicitNonWhitespaceVersion(string? version)
    {
        using var caller = new CancellationTokenSource();
        var source = new CapturingSource(AdmittedBody("template.invoice", "1.0.0", "PINNED-ONE"));
        var runtime = new DocumentRuntime(source, Surfaces());

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.RenderForIssuanceAsync(new("template.invoice", version), Flow, Context, caller.Token));

        Assert.Equal("documents.template.version_required", refused.Message);
        Assert.Empty(source.Calls);

        await AssertRender(runtime, true, new("template.invoice", "1.0.0"), "1.0.0", "PINNED-ONE", caller.Token);
        AssertCall(source, "template.invoice", "1.0.0", caller.Token);
    }

    [Fact(DisplayName = "documents-eng-10, documents-run-2 template-reference: a null issuance reference refuses before resolution")]
    public async Task NullIssuanceReferenceNeverResolves()
    {
        var source = new CapturingSource(null);
        var runtime = new DocumentRuntime(source, Surfaces());

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await runtime.RenderForIssuanceAsync(null!, Flow, Context));

        Assert.Empty(source.Calls);
    }

    private static byte[] AdmittedBody(string key, string version, string brand)
    {
        var template = Template(version);
        template = template with { Envelope = template.Envelope with { Identity = key }, Style = new(brand) };
        Assert.Empty(TemplateDefinitionAdmission.Validate(template, Surfaces())); // Public Validate admits at Publish.
        return TemplateDefinitionJson.SerializeCanonical(template);
    }

    private static async Task AssertRender(DocumentRuntime runtime, bool issuance, DocumentTemplateReference reference,
        string expectedVersion, string expectedBrand, CancellationToken token)
    {
        if (issuance)
        {
            var rendered = await runtime.RenderForIssuanceAsync(reference, Flow, Context, token);
            Assert.Equal("template.invoice", rendered.TemplateKey);
            Assert.Equal(expectedVersion, rendered.TemplateVersion);
            AssertDocument(rendered.Document, expectedBrand);
        }
        else
        {
            var preview = await runtime.PreviewAsync(reference, Flow, Context, null, token);
            AssertDocument(preview.Document, expectedBrand);
            Assert.False(preview.Minted);
        }
    }

    private static void AssertDocument(RenderedDocument document, string expectedBrand)
    {
        Assert.Equal("invoice", document.DocumentType);
        Assert.Equal("en-US", document.LocaleTag);
        Assert.Equal("USD", document.CurrencyCode);
        Assert.Equal(new TemplateStyle(expectedBrand), document.Style);
        Assert.Equal<RenderedDocumentBlock>([
            new("z-block", "first", "first literal"),
            new("a-block", "left", "second literal"),
        ], document.Blocks);
    }

    private static void AssertCall(CapturingSource source, string key, string? version, CancellationToken token)
    {
        var call = Assert.Single(source.Calls);
        Assert.Equal(key, call.Key);
        Assert.Equal(version, call.Version);
        Assert.Equal(token, call.Token);
        source.Calls.Clear();
    }

    private sealed class CapturingSource(byte[]? body) : IDocumentTemplateSource
    {
        public byte[]? Body { get; set; } = body;
        public List<(string Key, string? Version, CancellationToken Token)> Calls { get; } = [];

        public ValueTask<ReadOnlyMemory<byte>?> ResolvePublishedAsync(string key, string? version, CancellationToken cancellationToken = default)
        {
            Calls.Add((key, version, cancellationToken));
            return ValueTask.FromResult(Body is null ? (ReadOnlyMemory<byte>?)null : Body);
        }
    }
}
