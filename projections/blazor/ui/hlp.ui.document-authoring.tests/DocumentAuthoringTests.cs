using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AngleSharp.Dom;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.DocumentAuthoring;
using Harborline.Blocks.BuilderDefinitions;
using Xunit;
using Producer = Harborline.Foundation.Documents;
using Fixtures = Harborline.Foundation.Documents.Tests.TemplateFixtures;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class DocumentAuthoringTests : BunitContext
{
    private static readonly JsonNode Fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "document-authoring-fixtures", "fixtures.yaml")))!["cases"]![0]!;
    private static readonly DocumentCatalogue Catalogue = new() { Fields = ["record.total"], Regions = [new("masthead", "Masthead"), "folio"], Samples = [new("example", "Example invoice (sample)")], Records = ["record.1"] };
    private static readonly DocumentPageFlow Flow = new([new("block-1", "first", "Supplied page flow")]);
    private static readonly DocumentPreviewResult Result = new(false, "fr-FR", [new("block-1", "Total: Not supplied")]);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private IRenderedComponent<HarborlineDocumentAuthoringEditor> Editor(List<DocumentDraftChange> changes, DocumentDraft? value = null, bool readOnly = false, bool canPreview = true, DocumentCatalogue? catalogue = null, Func<DocumentPreviewRequest, Task<DocumentPreviewResult>>? preview = null, DocumentDenial? denial = null)
        => Render<HarborlineDocumentAuthoringEditor>(parameters => parameters.Add(component => component.Value, value ?? DocumentDraft.Empty).Add(component => component.PageFlow, Flow).Add(component => component.Catalogue, catalogue ?? Catalogue).Add(component => component.ReadOnly, readOnly).Add(component => component.CanPreview, canPreview).Add(component => component.Preview, preview ?? (_ => Task.FromResult(Result))).Add(component => component.Denial, denial).Add(component => component.DraftChanged, (DocumentDraftChange change) => changes.Add(change)));
    private static IElement Labelled(IRenderedComponent<HarborlineDocumentAuthoringEditor> cut, string label) => cut.Find($"[aria-label='{label}']");
    private static void Type(IRenderedComponent<HarborlineDocumentAuthoringEditor> cut, string label, string value)
    {
        var element = Labelled(cut, label);
        if (element.TagName == "INPUT") element.Input(value); else element.Change(value);
    }
    private static IElement Button(IRenderedComponent<HarborlineDocumentAuthoringEditor> cut, string label) => cut.FindAll("button").Single(button => button.TextContent.Trim() == label);
    private static void Replay(IRenderedComponent<HarborlineDocumentAuthoringEditor> cut)
    {
        foreach (var step in Fixture["input"]!["steps"]!.AsArray())
        {
            if (step!["set"] is { } set) Type(cut, set.GetValue<string>(), step["value"]!.GetValue<string>());
            else if (step["check"] is { } check) Labelled(cut, check.GetValue<string>()).Change(true);
            else Button(cut, step["click"]!.GetValue<string>()).Click();
        }
    }
    private static void Exact(DocumentDraft draft)
    {
        var actual = JsonSerializer.SerializeToNode(draft, Json)!; actual["revision"] = draft.Revision;
        Assert.True(JsonNode.DeepEquals(Fixture["expected"], actual), actual.ToJsonString());
    }

    [Fact(DisplayName = "documents-auth-1 documents-auth-3 T-594 item 4: fresh defaults and null text preserve the draft contract and edit generation")]
    public void FreshDefaultsAndNullText()
    {
        var value = DocumentDraft.Empty;
        Assert.Equal("", value.Identity); Assert.Null(value.Revision); Assert.Equal("", value.Name); Assert.Equal("", value.DocumentType);
        Assert.Equal(new("from_instance"), value.Locale); Assert.Equal(new(), value.Style); Assert.Empty(value.Blocks);
        var changes = new List<DocumentDraftChange>(); var cut = Editor(changes, value);
        Assert.Equal("hl-document-authoring", cut.Find("form").GetAttribute("class"));
        Assert.Contains("Local draft. Preview does not issue a document.", cut.Markup, StringComparison.Ordinal);
        Labelled(cut, "Template name").Input(new Microsoft.AspNetCore.Components.ChangeEventArgs());
        Assert.Equal("", Assert.Single(changes).Draft.Name); Assert.Equal(1, changes[0].Generation);
        Type(cut, "Template name", "Next"); Assert.Equal(2, changes[^1].Generation);
    }

    [Fact(DisplayName = "documents-eng-21 documents-auth-1 documents-auth-3 documents-auth-4 documents-auth-5 documents-auth-7 documents-auth-10 documents-auth-15 documents-auth-16 documents-ck-5 documents-ck-6 documents-ck-23 layout-ck-43: author from nothing matches the independent literal fixture")]
    public void AuthorsFromNothing()
    {
        var changes = new List<DocumentDraftChange>(); var cut = Editor(changes); Replay(cut); Exact(changes[^1].Draft);
        Assert.DoesNotContain("format", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Block label", cut.Markup, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "documents-auth-16: clearing a pinned logo produces no logo and passes authoritative admission")]
    public void ClearsLogo()
    {
        var changes = new List<DocumentDraftChange>(); var cut = Editor(changes);
        Type(cut, "Logo asset reference", "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"); Type(cut, "Logo asset reference", "");
        Assert.Null(changes[^1].Draft.Style.LogoAssetRef);
        var template = Fixtures.Template() with { Style = new(LogoAssetRef: changes[^1].Draft.Style.LogoAssetRef) };
        Assert.Empty(Producer.TemplateDefinitionAdmission.Validate(template, Fixtures.Surfaces()));
    }

    [Fact(DisplayName = "documents-auth-5 layout-ck-43: action names identify their block line and run, and edits stay isolated")]
    public void UniqueActionsAndIsolatedEdits()
    {
        var value = DocumentDraft.Empty with { Blocks = [new("first", [new([new(Text: "first")])]), new("second", [new([new(Text: "second")])])] };
        var changes = new List<DocumentDraftChange>(); var cut = Editor(changes, value);
        var names = cut.FindAll("button").Select(element => element.TextContent).ToArray(); Assert.Equal(names.Length, names.Distinct().Count());
        Assert.Equal("Literal", Labelled(cut, "Block 2 line 1 run 1 literal").ParentElement!.TextContent);
        Type(cut, "Block 2 line 1 run 1 literal", "Second edited");
        Assert.Equal("first", changes[^1].Draft.Blocks[0].Lines[0].Runs[0].Text); Assert.Equal("Second edited", changes[^1].Draft.Blocks[1].Lines[0].Runs[0].Text);
        Button(cut, "Remove run 1 from line 1 in block 2").Click();
        Assert.Equal("first", changes[^1].Draft.Blocks[0].Lines[0].Runs[0].Text); Assert.Empty(changes[^1].Draft.Blocks[1].Lines[0].Runs);
    }

    [Fact(DisplayName = "documents-auth-5 T-594 item 4: duplicate block ids are refused without discarding data and a valid revision recovers")]
    public void DuplicateBlocksRefuse()
    {
        var changes = new List<DocumentDraftChange>(); var requests = new List<DocumentPreviewRequest>();
        var cut = Editor(changes, DocumentDraft.Empty with { Blocks = [new("duplicate", []), new("duplicate", [])] }, preview: request => { requests.Add(request); return Task.FromResult(Result); });
        Assert.Equal("Duplicate block ids. The host must supply unique block ids.", cut.Find("[role='alert']").TextContent);
        Assert.Empty(cut.FindAll("form")); Assert.Empty(changes); Assert.Empty(requests);
        cut.Render(parameters => parameters.Add(component => component.Value, DocumentDraft.Empty with { Revision = "2" }));
        Assert.Empty(cut.FindAll("[role='alert']")); Assert.Single(cut.FindAll("form"));
    }

    [Fact(DisplayName = "documents-auth-5 layout-ck-43: ordered blocks, lines and runs remove exactly the selected item")]
    public void OrdersAndRemoves()
    {
        var changes = new List<DocumentDraftChange>(); var cut = Editor(changes); Replay(cut); Button(cut, "Add block").Click(); Button(cut, "Move block 2 up").Click();
        Assert.Equal(["block-2", "block-1"], changes[^1].Draft.Blocks.Select(block => block.Id));
        Button(cut, "Remove block 1").Click(); Button(cut, "Add block").Click(); Assert.Equal(["block-1", "block-2"], changes[^1].Draft.Blocks.Select(block => block.Id));
        Button(cut, "Add line to block 1").Click(); Button(cut, "Add literal to line 2 in block 1").Click(); Type(cut, "Block 1 line 2 run 1 literal", "Second line"); Button(cut, "Remove line 1 from block 1").Click();
        Assert.Equal("Second line", Assert.Single(Assert.Single(changes[^1].Draft.Blocks[0].Lines).Runs).Text);
        Button(cut, "Add merge to line 1 in block 1").Click(); Button(cut, "Remove run 1 from line 1 in block 1").Click(); Assert.Equal("", Assert.Single(changes[^1].Draft.Blocks[0].Lines[0].Runs).FieldPath);
        Button(cut, "Remove block 2").Click(); Assert.Equal("block-1", Assert.Single(changes[^1].Draft.Blocks).Id);
    }

    [Fact(DisplayName = "T-594 item 4 documents-ck-23: sm md lg container tokens and semantic breaks never depend on chrome window classes")]
    public void ContainerTokensAndBreaks()
    {
        var changes = new List<DocumentDraftChange>(); var cut = Editor(changes); Replay(cut);
        Assert.Equal(["", "sm", "md", "lg"], Labelled(cut, "Block 1 container token").QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
        Assert.True(changes[^1].Draft.Blocks[0].BreakBefore); Assert.True(changes[^1].Draft.Blocks[0].BreakAfter); Assert.Equal("avoid_page", changes[^1].Draft.Blocks[0].BreakInside);
        foreach (var token in new[] { "sm", "md", "lg", "" }) { Type(cut, "Block 1 container token", token); Assert.Equal(token.Length == 0 ? null : token, changes[^1].Draft.Blocks[0].CollapseBelow); }
        Labelled(cut, "Block 1 break before").Change(false); Labelled(cut, "Block 1 break after").Change(false); Type(cut, "Block 1 break inside", "auto");
        Assert.False(changes[^1].Draft.Blocks[0].BreakBefore); Assert.False(changes[^1].Draft.Blocks[0].BreakAfter); Assert.Equal("auto", changes[^1].Draft.Blocks[0].BreakInside);
    }

    [Fact(DisplayName = "documents-ck-5 documents-auth-3: changing locale policy clears only the fixed tag")]
    public void LocalePolicy()
    {
        var changes = new List<DocumentDraftChange>(); var cut = Editor(changes); Replay(cut); Type(cut, "Locale policy", "from_record"); Assert.Equal(new("from_record"), changes[^1].Draft.Locale); Assert.Empty(cut.FindAll("[aria-label='Document locale']"));
        Type(cut, "Locale policy", "from_instance"); Assert.Equal(new("from_instance"), changes[^1].Draft.Locale); Assert.Equal("Total: ", changes[^1].Draft.Blocks[0].Lines[0].Runs[0].Text);
    }

    [Fact(DisplayName = "documents-auth-15: all nine page master slots preserve their neighbours and can be cleared")]
    public void PageMasters()
    {
        var changes = new List<DocumentDraftChange>(); var cut = Editor(changes);
        foreach (var variant in new[] { "first", "left", "right" }) foreach (var region in new[] { "leftRegion", "centerRegion", "rightRegion" }) Type(cut, $"{variant} {region}", "folio");
        var master = new DocumentMasterVariant("folio", "folio", "folio"); Assert.Equal(new(master, master, master), changes[^1].Draft.Masters);
        Type(cut, "first centerRegion", ""); Assert.Equal(new("folio", null, "folio"), changes[^1].Draft.Masters.First);
    }

    [Fact(DisplayName = "T-594 item 4: string and object options, missing sections and unoffered current values")]
    public void OptionsAndFallbacks()
    {
        var changes = new List<DocumentDraftChange>(); var cut = Editor(changes); Replay(cut); cut.Render(parameters => parameters.Add(component => component.Catalogue, new DocumentCatalogue()));
        Assert.Equal(11, cut.FindAll("span").Count(element => element.TextContent == "No options offered."));
        Assert.Contains(cut.FindAll("option"), option => option.TextContent == "record.total (not offered)"); Assert.Contains(cut.FindAll("option"), option => option.TextContent == "masthead (not offered)");
        DocumentOption bare = "field"; Assert.Equal(new("field", "field"), bare);
    }

    [Fact(DisplayName = "T-594 item 4: readOnly emits no changes or preview even when events are dispatched")]
    public void ReadOnly()
    {
        var changes = new List<DocumentDraftChange>(); var requests = new List<DocumentPreviewRequest>(); var cut = Editor(changes, readOnly: true, preview: request => { requests.Add(request); return Task.FromResult(Result); });
        Type(cut, "Template name", "Changed"); Button(cut, "Add block").Click(); Button(cut, "Render preview").Click(); Assert.Empty(changes); Assert.Empty(requests); Assert.True(cut.Find("fieldset").HasAttribute("disabled"));
    }

    [Fact(DisplayName = "T-594 item 4: clean identity revision resets, dirty Keep Discard and readOnly pending transitions")]
    public void PendingEdits()
    {
        var changes = new List<DocumentDraftChange>(); var cut = Editor(changes); var first = DocumentDraft.Empty with { Identity = "other", Revision = "9007199254740993", Name = "Incoming" };
        cut.Render(parameters => parameters.Add(component => component.Value, first)); Assert.Equal("Incoming", Labelled(cut, "Template name").GetAttribute("value"));
        cut.Render(parameters => parameters.Add(component => component.Value, first with { Name = "Echo" })); Assert.Equal("Incoming", Labelled(cut, "Template name").GetAttribute("value")); Assert.Empty(changes);
        Type(cut, "Template name", "Local"); var incoming = first with { Revision = "3", Name = "Remote" }; cut.Render(parameters => parameters.Add(component => component.Value, incoming)); Assert.Single(cut.FindAll("[role='alert']"));
        Button(cut, "Keep edits").Click(); cut.Render(parameters => parameters.Add(component => component.Value, incoming with { })); Assert.Empty(cut.FindAll("[role='alert']"));
        incoming = incoming with { Identity = "new", Revision = "4" }; cut.Render(parameters => parameters.Add(component => component.Value, incoming).Add(component => component.ReadOnly, true)); Button(cut, "Discard edits").Click(); Button(cut, "Keep edits").Click(); Assert.Equal("Local", Labelled(cut, "Template name").GetAttribute("value")); Assert.True(Button(cut, "Discard edits").HasAttribute("disabled"));
        cut.Render(parameters => parameters.Add(component => component.ReadOnly, false)); Button(cut, "Discard edits").Click(); Assert.Equal(incoming, changes[^1].Draft); Assert.Equal("Remote", Labelled(cut, "Template name").GetAttribute("value"));
    }

    [Theory(DisplayName = "T-594 item 4: text input without blur survives incoming identity or revision and offers Keep Discard")]
    [InlineData("replacement", null)]
    [InlineData("", "2")]
    public void UnblurredTextSurvivesIncomingDraft(string identity, string? revision)
    {
        var changes = new List<DocumentDraftChange>(); var cut = Editor(changes);
        Labelled(cut, "Template name").Input("Typing without blur");
        var incoming = DocumentDraft.Empty with { Identity = identity, Revision = revision, Name = "Remote" };
        cut.Render(parameters => parameters.Add(component => component.Value, incoming));
        Assert.Equal("Typing without blur", Labelled(cut, "Template name").GetAttribute("value"));
        Assert.Equal("Typing without blur", Assert.Single(changes).Draft.Name);
        Assert.Single(cut.FindAll("[role='alert']"));
        Assert.False(Button(cut, "Keep edits").HasAttribute("disabled"));
        Assert.False(Button(cut, "Discard edits").HasAttribute("disabled"));
        Button(cut, "Discard edits").Click(); Assert.Equal(incoming, changes[^1].Draft);
        Assert.Equal("Remote", Labelled(cut, "Template name").GetAttribute("value"));
    }

    [Fact(DisplayName = "documents-eng-21: server denials are fenced to identity revision and local generation")]
    public void Denials()
    {
        var changes = new List<DocumentDraftChange>(); var denial = new DocumentDenial("", null, 0, [new("documents.denied", "/style")]); var cut = Editor(changes, denial: denial);
        Assert.Contains("documents.denied at /style", cut.Markup, StringComparison.Ordinal);
        foreach (var stale in new[] { denial with { Identity = "other" }, denial with { Revision = "2" }, denial with { Generation = 1 } }) { cut.Render(parameters => parameters.Add(component => component.Denial, stale)); Assert.Empty(cut.FindAll("[aria-label='Admission refusals']")); }
        cut.Render(parameters => parameters.Add(component => component.Denial, denial)); Type(cut, "Brand name", "Edited"); Assert.Empty(cut.FindAll("[aria-label='Admission refusals']"));
    }

    [Fact(DisplayName = "documents-eng-21 documents-auth-17 documents-run-2: server preview carries the authored draft supplied page flow and labelled sample")]
    public void PreviewRequest()
    {
        var changes = new List<DocumentDraftChange>(); var requests = new List<DocumentPreviewRequest>(); var cut = Editor(changes, preview: request => { requests.Add(request); return Task.FromResult(Result); }); Replay(cut); Type(cut, "Preview target", "example"); Button(cut, "Render preview").Click();
        var request = Assert.Single(requests); Exact(request.Draft); Assert.Same(Flow, request.PageFlow); Assert.Equal("preview", request.Mode); Assert.Equal(new("sample", "example"), request.Target);
        Assert.Equal("fr-FR", Labelled(cut, "Document preview").GetAttribute("lang")); Assert.Contains("Total: Not supplied", cut.Markup, StringComparison.Ordinal); Assert.Contains("Preview only. No document issued.", cut.Markup, StringComparison.Ordinal);
        Type(cut, "Preview source", "record"); Assert.True(Button(cut, "Render preview").HasAttribute("disabled")); Type(cut, "Preview target", "record.1"); Button(cut, "Render preview").Click(); Assert.Equal(new("record", "record.1"), requests[^1].Target);
    }

    [Fact(DisplayName = "documents-auth-17 documents-run-2: preview options follow their source and pending and completed status are explicit")]
    public async Task PreviewOptionsAndStatus()
    {
        var pending = new TaskCompletionSource<DocumentPreviewResult>(); var cut = Editor([], preview: _ => pending.Task);
        Assert.Single(Labelled(cut, "Preview target").QuerySelectorAll("option[value='example']"));
        Assert.Empty(Labelled(cut, "Preview target").QuerySelectorAll("option[value='record.1']"));
        Type(cut, "Preview source", "record");
        Assert.Single(Labelled(cut, "Preview target").QuerySelectorAll("option[value='record.1']"));
        Assert.Empty(Labelled(cut, "Preview target").QuerySelectorAll("option[value='example']"));
        Type(cut, "Preview target", "record.1");
        Assert.Single(Labelled(cut, "Preview target").QuerySelectorAll("option[value='record.1']"));
        var click = Button(cut, "Render preview").ClickAsync(new());
        Assert.Equal("Rendering preview…", cut.Find("[role='status']").TextContent); Assert.True(Button(cut, "Render preview").HasAttribute("disabled"));
        pending.SetResult(Result); await click;
        Assert.Equal("Preview only. No document issued.", cut.Find("[role='status']").TextContent); Assert.False(Button(cut, "Render preview").HasAttribute("disabled"));
    }

    [Fact(DisplayName = "documents-run-2: denied authority failure retry and minted responses fail closed")]
    public void PreviewDenial()
    {
        var changes = new List<DocumentDraftChange>(); var calls = 0; var cut = Editor(changes, canPreview: false, preview: _ => { calls++; return calls == 1 ? Task.FromException<DocumentPreviewResult>(new InvalidOperationException("secret detail")) : Task.FromResult(calls == 2 ? Result with { Minted = true } : Result); });
        Type(cut, "Preview target", "example"); Button(cut, "Render preview").Click(); Assert.Equal(0, calls);
        cut.Render(parameters => parameters.Add(component => component.CanPreview, true)); Button(cut, "Render preview").Click(); Assert.Contains("Preview refused.", cut.Markup, StringComparison.Ordinal); Assert.DoesNotContain("secret detail", cut.Markup, StringComparison.Ordinal);
        Button(cut, "Render preview").Click(); Assert.Empty(cut.FindAll("[aria-label='Document preview']")); Button(cut, "Render preview").Click(); Assert.Single(cut.FindAll("[aria-label='Document preview']"));
    }

    [Fact(DisplayName = "documents-run-2 T-594 item 4: stale async previews never replace a later edit or revoked authority")]
    public async Task StalePreview()
    {
        var pending = new TaskCompletionSource<DocumentPreviewResult>(); var cut = Editor([], preview: _ => pending.Task); Type(cut, "Preview target", "example");
        var click = Button(cut, "Render preview").ClickAsync(new()); Type(cut, "Template name", "Later"); pending.SetResult(Result); await click; Assert.Empty(cut.FindAll("[aria-label='Document preview']"));
        pending = new(); click = Button(cut, "Render preview").ClickAsync(new()); cut.Render(parameters => parameters.Add(component => component.CanPreview, false)); pending.SetResult(Result); await click; Assert.Empty(cut.FindAll("[aria-label='Document preview']"));
    }

    [Fact(DisplayName = "documents-run-2 T-594 item 4: target flow pending replacement and readOnly invalidate outstanding preview requests")]
    public async Task PreviewContextChanges()
    {
        var pending = new TaskCompletionSource<DocumentPreviewResult>(); var calls = 0; var cut = Editor([], preview: _ => { calls++; return pending.Task; }); Type(cut, "Preview target", "example");
        var click = Button(cut, "Render preview").ClickAsync(new()); Type(cut, "Preview source", "record"); pending.SetResult(Result); await click; Assert.Empty(cut.FindAll("[aria-label='Document preview']"));
        pending = new(); Type(cut, "Preview target", "record.1"); click = Button(cut, "Render preview").ClickAsync(new()); cut.Render(parameters => parameters.Add(component => component.PageFlow, new DocumentPageFlow([]))); pending.SetResult(Result); await click; Assert.Empty(cut.FindAll("[aria-label='Document preview']"));
        pending = new(); click = Button(cut, "Render preview").ClickAsync(new()); Type(cut, "Template name", "Unsaved"); cut.Render(parameters => parameters.Add(component => component.Value, DocumentDraft.Empty with { Revision = "2" })); pending.SetResult(Result); await click; Assert.Empty(cut.FindAll("[aria-label='Document preview']"));
        Assert.True(Button(cut, "Render preview").HasAttribute("disabled")); Button(cut, "Render preview").Click(); Assert.Equal(3, calls);
        Button(cut, "Keep edits").Click(); pending = new(); click = Button(cut, "Render preview").ClickAsync(new()); cut.Render(parameters => parameters.Add(component => component.ReadOnly, true)); pending.SetResult(Result); await click; Assert.Empty(cut.FindAll("[aria-label='Document preview']"));
    }

    [Fact(DisplayName = "documents-eng-21: authoring screen places the editor in the host supplied shell")]
    public void ScreenUsesHostShell()
    {
        var cut = Render<HarborlineDocumentAuthoringScreen>(parameters => parameters.Add(component => component.Value, DocumentDraft.Empty).Add(component => component.PageFlow, Flow).Add(component => component.Preview, _ => Task.FromResult(Result)).Add(component => component.Shell, body => builder => { builder.OpenElement(0, "main"); builder.AddContent(1, body); builder.CloseElement(); }));
        Assert.Single(cut.FindAll("main form[aria-label='Document template draft']"));
    }

    [Theory(DisplayName = "layout-ck-43 documents-auth-7 documents-auth-10 documents-auth-17 documents-run-2: authored runs use DocumentRuntime non-minting preview under two document locales")]
    [InlineData("en-US"), InlineData("fr-FR")]
    public async Task AuthoringUsesAuthoritativeRuntime(string locale)
    {
        var changes = new List<DocumentDraftChange>(); var source = new NoPublishedLookup(); string? canonicalValue = null;
        var cut = Editor(changes, preview: async request =>
        {
            Assert.Equal("preview", request.Mode); Assert.Same(Flow, request.PageFlow);
            var draft = request.Draft; var surface = Surface(draft);
            LayoutDefinitionAdmission.ValidateForPublish(surface, Harborline.Foundation.Documents.Tests.GrantsAllAuthor.Instance);
            var template = Fixtures.Template() with { DocumentType = draft.DocumentType, Locale = new(Producer.TemplateLocaleKind.Fixed, draft.Locale.Tag), Style = new(draft.Style.BrandName, draft.Style.LogoAssetRef), Envelope = Fixtures.Template().Envelope with { Identity = draft.Name } };
            var runtime = new Producer.DocumentRuntime(source, Fixtures.Surfaces(surface));
            var flow = new Producer.DocumentPageFlow(request.PageFlow.Blocks.Select(placed => new Producer.DocumentFlowBlock(placed.Id, placed.PageVariant, placed.Content, draft.Blocks.Single(block => block.Id == placed.Id).Lines[0].Runs.Select(run => run.Text is not null ? (Producer.DocumentFlowRun)new Producer.DocumentLiteralRun(run.Text) : new Producer.DocumentMergeRun(canonicalValue, Producer.DocumentMergeFormat.Text, run.Fallback)).ToArray())).ToArray());
            var preview = await runtime.PreviewAsync(new Producer.DocumentInlineTemplate(Producer.TemplateDefinitionJson.SerializeCanonical(template)), flow, new(null, "en-US", "USD"), "ja-JP");
            Assert.False(preview.Minted); Assert.Equal(locale, preview.Document.LocaleTag); Assert.Equal("invoice", preview.Document.DocumentType);
            return new(preview.Minted, preview.Document.LocaleTag, preview.Document.Blocks.Select(block => new DocumentPreviewBlock(block.Id, block.Content)).ToArray());
        });
        Replay(cut); Type(cut, "Document locale", locale); Type(cut, "Preview target", "example");
        foreach (var value in new string?[] { null, "", "1234.50" })
        {
            canonicalValue = value; await Button(cut, "Render preview").ClickAsync(new());
            Assert.Equal(value is null ? "Total: Not supplied" : "Total: " + value, Labelled(cut, "Document preview").QuerySelector("p")!.TextContent);
            Assert.Equal(locale, Labelled(cut, "Document preview").GetAttribute("lang"));
        }
        Assert.Equal(0, source.Calls);
    }

    [Theory(DisplayName = "T-594 item 4 documents-ck-23: authored container tokens pass shared numeric admission; illegal numeric constraints still refuse")]
    [InlineData("sm"), InlineData("md"), InlineData("lg")]
    public void LayoutAdmission(string token)
    {
        var changes = new List<DocumentDraftChange>(); var cut = Editor(changes); Replay(cut); Type(cut, "Block 1 container token", token); var surface = Surface(changes[^1].Draft);
        Assert.Equal(token, changes[^1].Draft.Blocks[0].CollapseBelow);
        LayoutDefinitionAdmission.ValidateForPublish(surface, Harborline.Foundation.Documents.Tests.GrantsAllAuthor.Instance);
        var invalid = surface with { Blocks = [surface.Blocks[0] with { Container = surface.Blocks[0].Container! with { ColumnCount = 0 } }, .. surface.Blocks.Skip(1)] };
        Assert.Throws<DefinitionRefusalException>(() => LayoutDefinitionAdmission.ValidateForPublish(invalid, Harborline.Foundation.Documents.Tests.GrantsAllAuthor.Instance));
    }

    private static LayoutDefinition Surface(DocumentDraft draft)
    {
        var blocks = draft.Blocks.Select(block => new LayoutBlock(block.Id, "layout.text", new LayoutStaticBinding(Fixtures.Json("\"\"")), block.Lines.Select((line, index) => new LayoutBlock($"{block.Id}-line-{index}", "layout.text", new LayoutTextBinding(line.Runs.Select(run => new LayoutTextRun(run.Text, run.FieldPath, run.Fallback)).ToArray()), [])).ToArray(), Container: new(LayoutContainerKind.Stack, CollapseBelow: block.CollapseBelow is null ? null : Enum.Parse<LayoutCollapseToken>(block.CollapseBelow, true)), BreakBefore: block.BreakBefore, BreakAfter: block.BreakAfter, BreakInside: block.BreakInside == "avoid_page" ? LayoutBreakInside.AvoidPage : LayoutBreakInside.Auto)).ToArray();
        var surface = Fixtures.Surface(blocks: blocks);
        return surface with { Blocks = [.. blocks, new("masthead-region", "layout.text", new LayoutStaticBinding(Fixtures.Json("\"Header\"")), [], FlowRole: LayoutFlowRole.Static, StaticRegion: "masthead"), new("folio-region", "layout.text", new LayoutStaticBinding(Fixtures.Json("\"Folio\"")), [], FlowRole: LayoutFlowRole.Static, StaticRegion: "folio")], PageMasters = [new("default", "a4", Master(draft.Masters.First), Master(draft.Masters.Left), Master(draft.Masters.Right))] };
    }
    private static LayoutMasterVariant Master(DocumentMasterVariant variant) => new(variant.LeftRegion, variant.CenterRegion, variant.RightRegion);
    private sealed class NoPublishedLookup : Producer.IDocumentTemplateSource
    {
        public int Calls { get; private set; }
        public ValueTask<ReadOnlyMemory<byte>?> ResolvePublishedAsync(string key, string? version, CancellationToken cancellationToken = default) { Calls++; return ValueTask.FromResult<ReadOnlyMemory<byte>?>(null); }
    }

    [Theory(DisplayName = "documents-eng-21: shared literal cross-lane contract fixture"), Trait("ModuleConformance", "hlp.ui.document-authoring")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.document-authoring", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixture(string caseId, string? rawFixture) => SharedFixtureBatch.Run(caseId, rawFixture, () => { var changes = new List<DocumentDraftChange>(); var cut = Editor(changes); Replay(cut); Exact(changes[^1].Draft); });
}
