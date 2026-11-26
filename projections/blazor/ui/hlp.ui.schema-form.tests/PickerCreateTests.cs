using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class PickerCreateTests : BunitContext
{
    private static readonly RecordCreateCapability Granted = new("asset", "asset", "an asset", true);
    public PickerCreateTests()
    {
        JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/select-field.js").Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/schema-form.js").Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<HarborlineSchemaForm> RenderPicker(RecordCreateCapability? capability,
        Action<RecordCreateRequest>? request = null, string[]? members = null)
        => Render<HarborlineSchemaForm>(p => p
            .Add(c => c.View, new SchemaFormView { FormId = "picker", Version = "1", Sections = [
                new() { Id = "main", Title = SchemaFormText.From("Main"), Fields = [
                    new() { Name = "asset", Label = SchemaFormText.From("Asset"), ControlHint = "RecordPicker",
                        PermittedValues = members ?? ["Alpha", "Alpine", "Beta"] }] }] })
            .Add(c => c.InitialValues, new Dictionary<string, object?> { ["asset"] = "Beta", ["other"] = "unsaved" })
            .Add(c => c.RecordCreateCapabilities, capability is null ? new Dictionary<string, RecordCreateCapability>()
                : new Dictionary<string, RecordCreateCapability> { ["asset"] = capability })
            .Add(c => c.OnRecordCreate, value => request?.Invoke(value))
            .Add(c => c.OnSubmit, _ => ValueTask.FromResult<SchemaFormValidationResult?>(null)));

    [Fact]
    public void PE1_Create_follows_matches_and_requests_once_without_changing_values()
    {
        var requests = new List<RecordCreateRequest>();
        var cut = RenderPicker(Granted, requests.Add);
        IReadOnlyDictionary<string, object?>? submitted = null;
        cut.Render(p => p.Add(c => c.OnSubmit, values => { submitted = values; return ValueTask.FromResult<SchemaFormValidationResult?>(null); }));
        var input = cut.Find("[role=combobox]");
        input.Input("Al");
        Assert.Equal(["Alpha", "Alpine", "Create asset 'Al'"], cut.FindAll("[role=option]").Select(o => o.TextContent.Trim()));
        input.KeyDown("ArrowUp"); input.KeyDown("Enter");
        Assert.Equal(new RecordCreateRequest("asset", "asset", "Al"), Assert.Single(requests));
        cut.Find("form").Submit();
        Assert.NotNull(submitted);
        Assert.Equal("Beta", submitted["asset"]);
        Assert.Equal("unsaved", submitted["other"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PE2_Absent_or_denied_capability_never_offers_create(bool denied)
    {
        var requests = new List<RecordCreateRequest>();
        var cut = RenderPicker(denied ? Granted with { CanCreate = false } : null, requests.Add);
        cut.Find("[role=combobox]").Input("missing");
        Assert.Empty(cut.FindAll("[role=option]"));
        Assert.Empty(requests);
    }

    [Fact]
    public void PE3_Denied_empty_matches_explain_prerequisite_but_matches_do_not()
    {
        var cut = RenderPicker(Granted with { CanCreate = false });
        var input = cut.Find("[role=combobox]"); input.Input("missing");
        Assert.Equal("No asset you can see match 'missing'. Creating an asset needs permission you don't have.", cut.Find("[role=status]").TextContent);
        input.Input("Al");
        Assert.Empty(cut.FindAll("[role=status]"));
    }

    [Fact]
    public void PE4_Empty_and_whitespace_text_offer_no_create()
    {
        var cut = RenderPicker(Granted);
        var input = cut.Find("[role=combobox]"); input.Focus();
        Assert.DoesNotContain("Create asset", cut.Markup, StringComparison.Ordinal);
        input.Input("   ");
        Assert.DoesNotContain("Create asset", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_domain_still_allows_creation()
    {
        var cut = RenderPicker(Granted, members: []);
        cut.Find("[role=combobox]").Input("New");
        Assert.Equal("Create asset 'New'", cut.Find("[role=option]").TextContent.Trim());
    }

    [Fact]
    public void Revocation_removes_the_active_create_action_and_uses_localized_prerequisite()
    {
        var requests = new List<RecordCreateRequest>();
        var cut = RenderPicker(Granted, requests.Add);
        cut.Render(p => p.Add(c => c.Strings, new SchemaFormStrings {
            RecordCreate = "Ajouter {kind}: {text}", RecordCreatePrerequisite = "Permission requise: {indefiniteKind} ({text})"
        }));
        var input = cut.Find("[role=combobox]"); input.Input("New");
        Assert.Equal("Ajouter asset: New", cut.Find("[role=option]").TextContent.Trim());
        input.KeyDown("ArrowUp");
        cut.Render(p => p.Add(c => c.RecordCreateCapabilities, new Dictionary<string, RecordCreateCapability> {
            ["asset"] = Granted with { CanCreate = false }
        }));
        input.KeyDown("Enter");
        Assert.Empty(requests);
        Assert.Equal("Permission requise: an asset (New)", cut.Find("[role=status]").TextContent);
    }

    [Fact]
    public void Pointer_create_emits_once_and_disabled_form_cannot_request_again()
    {
        var requests = new List<RecordCreateRequest>();
        var cut = RenderPicker(Granted, requests.Add);
        cut.Find("[role=combobox]").Input("Al");
        cut.Find("[data-hl-action]").Click();
        Assert.Equal(new RecordCreateRequest("asset", "asset", "Al"), Assert.Single(requests));
        cut.Render(p => p.Add(c => c.Disabled, true));
        var input = cut.Find("[role=combobox]"); input.Input("New"); input.KeyDown("ArrowUp"); input.KeyDown("Enter");
        Assert.Single(requests);
    }

    [Fact]
    public void PE5_Shared_picker_corpus_produces_normalized_actions()
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Harborline.Platform.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var file = Path.Combine(root.FullName, "projections/react/ui/hlp.ui.schema-form/src/__tests__/picker-create-cases.json");
        using var corpus = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
        var actual = new List<object>();
        foreach (var row in corpus.RootElement.EnumerateArray())
        {
            var permission = row.GetProperty("permission").GetString();
            var cap = permission == "absent" ? null : Granted with { CanCreate = permission == "granted" };
            using var cut = RenderPicker(cap, members: row.GetProperty("members").EnumerateArray().Select(v => v.GetString()!).ToArray());
            cut.Render(p => p.Add(c => c.Values, new Dictionary<string, object?>()));
            var input = cut.Find("[role=combobox]"); input.Focus();
            var query = row.GetProperty("query").GetString()!;
            if (query.Length > 0) input.Input(query);
            var actions = cut.FindAll("[role=option]").Select(option => new {
                kind = option.HasAttribute("data-hl-action") ? "create" : "match", label = option.TextContent.Trim()
            }).ToArray();
            Assert.Equal(row.GetProperty("expected").EnumerateArray().Select(value =>
                (value.GetProperty("kind").GetString(), value.GetProperty("label").GetString())),
                actions.Select(value => ((string?)value.kind, (string?)value.label)));
            actual.Add(new { id = row.GetProperty("id").GetString(), actions });
        }
        var output = Environment.GetEnvironmentVariable("HARBORLINE_PICKER_PARITY_OUT");
        if (output is not null) File.WriteAllText(Path.Combine(output, "blazor-actions.json"), System.Text.Json.JsonSerializer.Serialize(actual));
    }
}
