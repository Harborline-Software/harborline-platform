using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.RecordsAuthoring;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class RecordTypeCategoriesTests : BunitContext
{
    private static RecordsDraft Draft(string body, string? revision = "7") => new("eam.category-type", "1.0.0", revision, "Records", JsonNode.Parse(body)!.AsObject());
    private IRenderedComponent<HarborlineRecordTypeDraftEditor> Editor(RecordsDraft value, List<RecordsDraftChange> changes, bool readOnly = false, RecordsDiagnosticSet? diagnostics = null)
        => Render<HarborlineRecordTypeDraftEditor>(parameters => parameters.Add(component => component.Value, value).Add(component => component.ReadOnly, readOnly).Add(component => component.Diagnostics, diagnostics).Add(component => component.DraftChanged, (RecordsDraftChange change) => changes.Add(change)));
    private static IElement Button(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, string text) => cut.FindAll("button").Single(button => button.TextContent.Trim() == text);
    private static IElement Category(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, int n) => cut.Find($"input[aria-label='Category {n}']");
    private static string? Generation(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut) => cut.Find("form").GetAttribute("data-generation");
    private static void AssertBody(string expected, JsonObject actual) => Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), actual.ToJsonString());
    private static void AssertChange(RecordsDraftChange change, string expected, int generation, string? revision = "7")
    {
        Assert.Equal(("eam.category-type", "1.0.0", revision, (string?)"Records", generation), (change.Draft.Identity, change.Draft.VersionId, change.Draft.ExpectedRevision, change.Draft.DefinitionKind, change.Generation));
        AssertBody(expected, change.Draft.Body);
    }

    [Theory]
    [InlineData("""{"name":"Current","future":{"keep":[null,17]}}""", """{"name":"Current","future":{"keep":[null,17]},"categories":[""]}""")]
    [InlineData("""{"name":"Current","categories":null,"future":{"keep":[null,17]}}""", """{"name":"Current","categories":[""],"future":{"keep":[null,17]}}""")]
    [InlineData("""{"name":"Current","categories":[]}""", """{"name":"Current","categories":[""]}""")]
    [InlineData("""{"name":"Current","categories":["same","same"]}""", """{"name":"Current","categories":["same","same",""]}""")]
    [InlineData("""{"name":"Current","categories":[null,17,{"raw":true},["nested"],"  Café  "]}""", """{"name":"Current","categories":[null,17,{"raw":true},["nested"],"  Café  ",""]}""")]
    public void Explicit_Add_creates_or_appends_one_empty_string_and_preserves_the_rest(string input, string expected)
    {
        var draft = Draft(input); var changes = new List<RecordsDraftChange>(); var cut = Editor(draft, changes);
        Assert.Empty(changes); Assert.Equal("0", Generation(cut));
        Button(cut, "Add category").Click();
        AssertChange(Assert.Single(changes), expected, 1);
        Assert.Equal("1", Generation(cut)); AssertBody(input, draft.Body);
    }

    [Theory]
    [InlineData("""{"name":"Current","future":[null,17]}""", """{"name":"Renamed","future":[null,17]}""")]
    [InlineData("""{"name":"Current","categories":null}""", """{"name":"Renamed","categories":null}""")]
    [InlineData("""{"name":"Current","categories":17}""", """{"name":"Renamed","categories":17}""")]
    [InlineData("""{"name":"Current","categories":false}""", """{"name":"Renamed","categories":false}""")]
    [InlineData("""{"name":"Current","categories":"  raw  "}""", """{"name":"Renamed","categories":"  raw  "}""")]
    [InlineData("""{"name":"Current","categories":{"raw":[null,"x"]}}""", """{"name":"Renamed","categories":{"raw":[null,"x"]}}""")]
    [InlineData("""{"name":"Current","categories":[null,"duplicate","duplicate"]}""", """{"name":"Renamed","categories":[null,"duplicate","duplicate"]}""")]
    public void Rendering_and_an_unrelated_edit_leave_the_categories_member_as_supplied(string input, string expected)
    {
        var draft = Draft(input); var changes = new List<RecordsDraftChange>(); var cut = Editor(draft, changes);
        Assert.Empty(changes); Assert.Equal("0", Generation(cut));
        cut.Find("input[aria-label='Record type name']").Change("Renamed");
        AssertChange(Assert.Single(changes), expected, 1); AssertBody(input, draft.Body);
    }

    [Theory]
    [InlineData("17")]
    [InlineData("false")]
    [InlineData("\"raw\"")]
    [InlineData("{\"raw\":[null,17]}")]
    public void Nonarray_categories_have_no_editable_rows_and_Add_is_disabled_without_repair(string categories)
    {
        var input = "{\"name\":\"Current\",\"categories\":" + categories + ",\"future\":true}";
        var draft = Draft(input); var changes = new List<RecordsDraftChange>(); var cut = Editor(draft, changes);
        Assert.Empty(cut.FindAll("input[aria-label^='Category ']"));
        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Trim().StartsWith("Remove category", StringComparison.Ordinal));
        Assert.Equal(categories, cut.Find("output[aria-label='Categories']").TextContent);
        Assert.True(Button(cut, "Add category").HasAttribute("disabled"));
        Button(cut, "Add category").Click();
        Assert.Empty(changes); Assert.Equal("0", Generation(cut)); AssertBody(input, draft.Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t  literal \n")]
    [InlineData("Café 東京")]
    [InlineData("duplicate")]
    public void Only_strings_are_editable_at_original_array_indices_and_text_is_literal(string text)
    {
        const string input = """{"name":"Current","categories":[null,17,{"raw":[true]},["nested"],"old","duplicate","duplicate"],"future":{"keep":true}}""";
        var draft = Draft(input, "9007199254740993"); var changes = new List<RecordsDraftChange>(); var cut = Editor(draft, changes);
        Assert.Equal(["Category 5", "Category 6", "Category 7"], cut.FindAll("input[aria-label^='Category ']").Select(element => element.GetAttribute("aria-label")!).ToArray());
        Assert.Equal(["null", "17", "{\"raw\":[true]}", "[\"nested\"]"], cut.FindAll("output[aria-label^='Category ']").Select(output => output.TextContent).ToArray());
        Category(cut, 5).Change(text);
        var change = Assert.Single(changes);
        Assert.Equal(("eam.category-type", "1.0.0", (string?)"9007199254740993", (string?)"Records", 1), (change.Draft.Identity, change.Draft.VersionId, change.Draft.ExpectedRevision, change.Draft.DefinitionKind, change.Generation));
        Assert.Equal(text, change.Draft.Body["categories"]![4]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""[null,17,{"raw":[true]},["nested"]]"""), new JsonArray(change.Draft.Body["categories"]!.AsArray().Take(4).Select(node => node?.DeepClone()).ToArray())));
        Assert.Equal(["duplicate", "duplicate"], change.Draft.Body["categories"]!.AsArray().Skip(5).Select(node => node!.GetValue<string>()).ToArray());
        AssertBody("""{"keep":true}""", change.Draft.Body["future"]!.AsObject());
        Assert.Equal(text, Category(cut, 5).GetAttribute("value")); Assert.Equal("1", Generation(cut)); AssertBody(input, draft.Body);
    }

    [Theory]
    [InlineData(1, """[null,{"raw":true},false,["nested"],"duplicate","tail"]""")]
    [InlineData(2, """["duplicate",{"raw":true},false,["nested"],"duplicate","tail"]""")]
    [InlineData(3, """["duplicate",null,false,["nested"],"duplicate","tail"]""")]
    [InlineData(4, """["duplicate",null,{"raw":true},["nested"],"duplicate","tail"]""")]
    [InlineData(5, """["duplicate",null,{"raw":true},false,"duplicate","tail"]""")]
    [InlineData(6, """["duplicate",null,{"raw":true},false,["nested"],"tail"]""")]
    [InlineData(7, """["duplicate",null,{"raw":true},false,["nested"],"duplicate"]""")]
    public void Remove_deletes_only_the_addressed_original_index_including_malformed_nodes_or_one_duplicate(int n, string expectedCategories)
    {
        const string input = """{"name":"Current","categories":["duplicate",null,{"raw":true},false,["nested"],"duplicate","tail"],"future":{"keep":[null,17]}}""";
        var draft = Draft(input); var changes = new List<RecordsDraftChange>(); var cut = Editor(draft, changes);
        Button(cut, $"Remove category {n}").Click();
        AssertChange(Assert.Single(changes), "{\"name\":\"Current\",\"categories\":" + expectedCategories + ",\"future\":{\"keep\":[null,17]}}", 1);
        AssertBody(input, draft.Body);
    }

    [Fact]
    public void Successive_changes_copy_whole_bodies_and_never_mutate_host_or_prior_callbacks()
    {
        const string input = """{"name":"Current","categories":["a"],"fields":[null,{"future":17}],"future":{"keep":[1,"two"]}}""";
        var draft = Draft(input); var changes = new List<RecordsDraftChange>(); var cut = Editor(draft, changes);
        Button(cut, "Add category").Click(); Category(cut, 2).Change("b"); Button(cut, "Remove category 1").Click();
        Assert.Equal(3, changes.Count);
        AssertChange(changes[0], """{"name":"Current","categories":["a",""],"fields":[null,{"future":17}],"future":{"keep":[1,"two"]}}""", 1);
        AssertChange(changes[1], """{"name":"Current","categories":["a","b"],"fields":[null,{"future":17}],"future":{"keep":[1,"two"]}}""", 2);
        AssertChange(changes[2], """{"name":"Current","categories":["b"],"fields":[null,{"future":17}],"future":{"keep":[1,"two"]}}""", 3);
        AssertBody(input, draft.Body); Assert.Equal("3", Generation(cut)); Assert.Equal("b", Category(cut, 1).GetAttribute("value"));
    }

    [Fact]
    public void Read_only_category_controls_and_handlers_emit_nothing()
    {
        const string input = """{"name":"Current","categories":["host"],"future":true}""";
        var draft = Draft(input); var changes = new List<RecordsDraftChange>(); var cut = Editor(draft, changes, readOnly: true);
        Assert.Single(cut.FindAll("fieldset.hl-records-authoring__read-only[disabled]"));
        Category(cut, 1).Change("ignored"); Button(cut, "Add category").Click(); Button(cut, "Remove category 1").Click();
        Assert.Empty(changes); Assert.Equal("0", Generation(cut)); Assert.Equal("host", Category(cut, 1).GetAttribute("value")); AssertBody(input, draft.Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("01")]
    [InlineData("9223372036854775808")]
    public void An_unadopted_rejected_revision_has_no_category_controls_or_callbacks(string revision)
    {
        const string input = """{"name":"Current","categories":["host"]}""";
        var changes = new List<RecordsDraftChange>(); var cut = Editor(Draft(input, revision), changes);
        Assert.Empty(cut.FindAll("input[aria-label^='Category ']")); Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Trim() == "Add category");
        Assert.Empty(changes); Assert.Equal("0", Generation(cut));
        cut.Render(parameters => parameters.Add(component => component.Value, Draft(input)));
        Assert.Empty(changes); Assert.Equal("0", Generation(cut)); Assert.Equal("host", Category(cut, 1).GetAttribute("value"));
        Button(cut, "Add category").Click();
        AssertChange(Assert.Single(changes), """{"name":"Current","categories":["host",""]}""", 1);
    }

    [Fact]
    public void Categories_wait_for_the_Records_kind_and_then_use_the_existing_whole_draft_change()
    {
        var draft = Draft("""{"name":"Current","categories":null,"future":true}""") with { DefinitionKind = null };
        var changes = new List<RecordsDraftChange>(); var cut = Editor(draft, changes);
        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Trim() == "Add category");
        cut.Find("select[aria-label='Definition kind']").Change("Records");
        AssertChange(Assert.Single(changes), """{"name":"Current","categories":null,"future":true}""", 1);
        Button(cut, "Add category").Click();
        Assert.Equal(2, changes.Count); AssertChange(changes[1], """{"name":"Current","categories":[""],"future":true}""", 2);
    }

    [Fact]
    public void Keep_preserves_local_categories_without_a_callback_and_Discard_adopts_the_complete_new_incoming_body()
    {
        const string initial = """{"name":"Current","categories":["host"],"future":{"original":true}}""";
        var draft = Draft(initial); var changes = new List<RecordsDraftChange>(); var cut = Editor(draft, changes);
        Category(cut, 1).Change("local");
        var incoming = Draft("""{"name":"Incoming","categories":["incoming"],"future":{"incoming":true}}""", "8");
        cut.Render(parameters => parameters.Add(component => component.Value, incoming));
        Assert.Single(changes); Assert.Single(cut.FindAll("[role='alert']")); Assert.Equal("local", Category(cut, 1).GetAttribute("value"));
        Button(cut, "Keep edits").Click();
        Assert.Single(changes); Assert.Empty(cut.FindAll("[role='alert']")); Assert.Equal("1", Generation(cut)); Assert.Equal("local", Category(cut, 1).GetAttribute("value"));
        cut.Render(parameters => parameters.Add(component => component.Value, incoming));
        Assert.Empty(cut.FindAll("[role='alert']")); Assert.Single(changes);
        var latest = Draft("""{"name":"Latest","categories":[null,"new"],"future":{"latest":[17]}}""", "9");
        cut.Render(parameters => parameters.Add(component => component.Value, latest));
        Button(cut, "Discard edits").Click();
        Assert.Equal(2, changes.Count); AssertChange(changes[1], """{"name":"Latest","categories":[null,"new"],"future":{"latest":[17]}}""", 2, "9");
        Assert.Equal("2", Generation(cut)); Assert.Equal("new", Category(cut, 2).GetAttribute("value")); AssertBody(initial, draft.Body);
    }

    [Fact]
    public void Clean_incoming_categories_replace_without_callback_then_the_next_edit_uses_the_new_body()
    {
        var changes = new List<RecordsDraftChange>(); var cut = Editor(Draft("""{"name":"Old","categories":["old"],"future":"old"}"""), changes);
        cut.Render(parameters => parameters.Add(component => component.Value, Draft("""{"name":"New","categories":["new"],"future":{"new":true}}""", "8")));
        Assert.Empty(changes); Assert.Equal("1", Generation(cut)); Assert.Equal("new", Category(cut, 1).GetAttribute("value"));
        Button(cut, "Add category").Click();
        AssertChange(Assert.Single(changes), """{"name":"New","categories":["new",""],"future":{"new":true}}""", 2, "8");
    }

    [Fact]
    public void Rejected_incoming_categories_do_not_replace_or_hold_the_adopted_draft()
    {
        var changes = new List<RecordsDraftChange>(); var cut = Editor(Draft("""{"name":"Current","categories":["host"]}"""), changes);
        cut.Render(parameters => parameters.Add(component => component.Value, Draft("""{"name":"Rejected","categories":{"raw":true}}""", "01")));
        Assert.Empty(changes); Assert.Empty(cut.FindAll("[role='alert']")); Assert.Equal("0", Generation(cut)); Assert.Equal("host", Category(cut, 1).GetAttribute("value"));
        Category(cut, 1).Change("local");
        AssertChange(Assert.Single(changes), """{"name":"Current","categories":["local"]}""", 1);
        cut.Render(parameters => parameters.Add(component => component.Value, Draft("""{"name":"Rejected again","categories":["ignored"]}""", "+7")));
        Assert.Single(changes); Assert.Empty(cut.FindAll("[role='alert']")); Assert.Equal("1", Generation(cut)); Assert.Equal("local", Category(cut, 1).GetAttribute("value"));
    }

    [Fact]
    public void Becoming_read_only_blocks_categories_and_pending_incoming_actions()
    {
        var changes = new List<RecordsDraftChange>(); var cut = Editor(Draft("""{"name":"Current","categories":["host"]}"""), changes);
        Category(cut, 1).Change("local");
        cut.Render(parameters => parameters.Add(component => component.Value, Draft("""{"name":"Incoming","categories":["incoming"]}""", "8")).Add(component => component.ReadOnly, true));
        Category(cut, 1).Change("ignored"); Button(cut, "Add category").Click(); Button(cut, "Remove category 1").Click(); Button(cut, "Keep edits").Click(); Button(cut, "Discard edits").Click();
        Assert.Single(changes); Assert.Equal("1", Generation(cut)); Assert.Single(cut.FindAll("[role='alert']")); Assert.Equal("local", Category(cut, 1).GetAttribute("value"));
    }

    [Theory]
    [InlineData("eam.other", "7", 0, false)]
    [InlineData("eam.category-type", "8", 0, false)]
    [InlineData("eam.category-type", "07", 0, false)]
    [InlineData("eam.category-type", "7", 1, false)]
    [InlineData("eam.category-type", "7", 0, true)]
    public void Category_edits_preserve_exact_diagnostic_identity_revision_and_generation_matching(string identity, string revision, int generation, bool shown)
    {
        var diagnostics = new RecordsDiagnosticSet(identity, revision, generation, [new("probe.category", "/categories/0") { Target = "target.kept" }]) { Warnings = [new("probe.warning", "/categories")] };
        var changes = new List<RecordsDraftChange>(); var cut = Editor(Draft("""{"name":"Current","categories":["host"]}"""), changes, diagnostics: diagnostics);
        if (shown)
        {
            Assert.Equal("probe.category at /categories/0 for target.kept", Assert.Single(cut.FindAll("ul[aria-label='Diagnostics'] li")).TextContent);
            Assert.Equal("probe.warning at /categories", Assert.Single(cut.FindAll("ul[aria-label='Warnings'] li")).TextContent);
        }
        else
        {
            Assert.Empty(cut.FindAll("ul[aria-label='Diagnostics'] li"));
            Assert.Empty(cut.FindAll("ul[aria-label='Warnings'] li"));
        }
        Assert.Empty(changes); Assert.Equal("0", Generation(cut));
        Button(cut, "Add category").Click();
        AssertChange(Assert.Single(changes), """{"name":"Current","categories":["host",""]}""", 1);
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, diagnostics with { Identity = "eam.category-type", ExpectedRevision = "7", Generation = 0 }));
        Assert.Empty(cut.FindAll("ul[aria-label='Diagnostics'] li")); Assert.Empty(cut.FindAll("ul[aria-label='Warnings'] li"));
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, diagnostics with { Identity = "eam.category-type", ExpectedRevision = "7", Generation = 1 }));
        Assert.Equal(["probe.category at /categories/0 for target.kept"], cut.FindAll("ul[aria-label='Diagnostics'] li").Select(item => item.TextContent).ToArray());
        Assert.Equal(["probe.warning at /categories"], cut.FindAll("ul[aria-label='Warnings'] li").Select(item => item.TextContent).ToArray());
        Assert.Single(changes); Assert.Equal("1", Generation(cut));
    }
}
