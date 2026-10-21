using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.RecordsAuthoring;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// M11 item 6: tests for the EXISTING field kind/version selector only. No production path changes. Every expected output is a literal.
// Behaviour under test (HarborlineRecordTypeDraftEditor.razor):
//   :22, :99   offered = index of the option whose KindId AND Version equal the field kind's kind_id AND version (ordinal ==), else -1.
//   :29        the select's value is the invariant index, or "" with a leading "{kind_id} {version} (not offered)" option.
//   :30, :100-102 string and number parameters render as inputs (numbers as their JSON text); other parameter values do not.
//   :114-119   ChooseFieldKind: int.TryParse(NumberStyles.None) and choice < KindOptions.Count guard the index; the chosen kind_id and
//              version are written into a deep copy of the existing kind object (:107), so parameters and unknown members survive.
//   :81-89     host values: an echo of the current identity/version/revision is not a replacement; a different one waits while dirty.
//   :105       Change: local edit, advances the local generation and emits RecordsDraftChange(draft, generation).
//   :134-141   Keep drops the incoming draft without emitting; Discard emits it at the next generation.
//   :94        diagnostics show only for the current identity, the ordinal-equal revision (null equals null) and the current generation.
//   :13        the revision is shown verbatim, or "None" when null.
// Paired one-for-one with projections/react/ui/hlp.ui.records-authoring/src/RecordTypeKindVersionSelector.test.tsx.
public sealed class RecordTypeKindVersionSelectorTests : BunitContext
{
    // The four rows of the test-host kind registry (conformance/hlp.ui.records-authoring/source-fixtures/records-current-grammar.json,
    // grammarFacts.testHostKindRegistry), written as literals.
    private static readonly IReadOnlyList<RecordsKindOption> Registry = [new("count", "1.0.0"), new("text", "1.0.0"), new("date", "1.0.0"), new("date", "2.0.0")];
    private static readonly (string?, string)[] RegistryOptions = [("0", "count 1.0.0"), ("1", "text 1.0.0"), ("2", "date 1.0.0"), ("3", "date 2.0.0")];
    private static readonly IReadOnlyList<RecordsDiagnostic> Probe = [new("probe.selector", "/fields/0/binding/kind")];
    private const string ProbeText = "probe.selector at /fields/0/binding/kind";

    private static JsonObject Field(string key, string kindJson) => new()
    {
        ["field_key"] = key,
        ["display_name"] = $"Display {key}",
        ["binding"] = new JsonObject { ["kind"] = JsonNode.Parse(kindJson), ["constraints"] = JsonNode.Parse("""{"required":false,"minimum_count":0,"maximum_count":1,"read_role_ids":[]}""") },
        ["future_field_member"] = "kept",
    };
    private static JsonObject BodyOf(params JsonObject[] fields)
    {
        var array = new JsonArray();
        foreach (var field in fields) array.Add(field);
        return new JsonObject { ["name"] = "Asset Class", ["future_member"] = JsonNode.Parse("""{"kept":[1,"two"]}"""), ["fields"] = array };
    }
    private static RecordsDraft DraftOf(string? revision, params JsonObject[] fields) => new("eam.asset-class", "1.0.0", revision, "Records", BodyOf(fields));
    private static RecordsDiagnosticSet ProbeAt(string revision, int generation) => new("eam.asset-class", revision, generation, Probe);

    private IRenderedComponent<HarborlineRecordTypeDraftEditor> Editor(RecordsDraft value, IReadOnlyList<RecordsKindOption> options, List<RecordsDraftChange> changes, RecordsDiagnosticSet? diagnostics = null)
        => Render<HarborlineRecordTypeDraftEditor>(parameters => parameters.Add(component => component.Value, value).Add(component => component.KindOptions, options).Add(component => component.Diagnostics, diagnostics).Add(component => component.DraftChanged, (RecordsDraftChange change) => changes.Add(change)));
    private static IElement KindSelect(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, int n) => cut.Find($"select[aria-label='Field {n} kind']");
    // The select's rendered value attribute, read the same way as the native P3 test (RecordsDraftTests.cs).
    private static string? Selected(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, int n) => KindSelect(cut, n).GetAttribute("value");
    private static (string?, string)[] OptionsOf(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, int n) => [.. cut.FindAll($"select[aria-label='Field {n} kind'] option").Select(option => (option.GetAttribute("value"), option.TextContent))];
    private static string? Generation(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut) => cut.Find("form").GetAttribute("data-generation");
    private static string Revision(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut) => cut.Find("output[aria-label='Expected revision']").TextContent;
    private static string[] Rendered(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut) => [.. cut.FindAll("ul[aria-label='Diagnostics'] li").Select(item => item.TextContent)];
    private static IElement Input(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, string label) => cut.Find($"input[aria-label='{label}']");
    private static IElement Button(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, string text) => cut.FindAll("button").Single(button => button.TextContent.Trim() == text);
    private static void AssertDraft(RecordsDraftChange change, string? revision, int generation, JsonObject body)
    {
        Assert.Equal(("eam.asset-class", "1.0.0", revision, (string?)"Records", generation), (change.Draft.Identity, change.Draft.VersionId, change.Draft.ExpectedRevision, change.Draft.DefinitionKind, change.Generation));
        Assert.True(JsonNode.DeepEquals(body, change.Draft.Body), change.Draft.Body.ToJsonString());
    }

    // Gap 1: the "(not offered)" branch (:29). Matching is exact on both members (:99): no case folding, no version normalisation.
    [Theory]
    [InlineData("{\"kind_id\":\"legacy\",\"version\":\"0.9.0\"}", "legacy 0.9.0 (not offered)")]
    [InlineData("{\"kind_id\":\"date\",\"version\":\"3.0.0\"}", "date 3.0.0 (not offered)")]
    [InlineData("{\"kind_id\":\"Date\",\"version\":\"1.0.0\"}", "Date 1.0.0 (not offered)")]
    [InlineData("{\"kind_id\":\"count\",\"version\":\"1.0\"}", "count 1.0 (not offered)")]
    [InlineData("{\"kind_id\":\"date\"}", "date  (not offered)")]
    [InlineData("{}", "  (not offered)")]
    public void Shows_the_selected_placeholder_for_a_kind_version_the_host_does_not_offer(string kindJson, string text)
    {
        var changes = new List<RecordsDraftChange>();
        var cut = Editor(DraftOf("9007199254740993", Field("gauge", kindJson), Field("label", """{"kind_id":"text","version":"1.0.0","parameters":{}}""")), Registry, changes);
        Assert.Equal([("", text), .. RegistryOptions], OptionsOf(cut, 1));
        Assert.Equal("", Selected(cut, 1));
        // An offered kind on another field gets no placeholder and is selected by its index.
        Assert.Equal(RegistryOptions, OptionsOf(cut, 2)); Assert.Equal("1", Selected(cut, 2));
        Assert.Empty(changes); Assert.Equal("0", Generation(cut));
    }

    // Gap 2: the "" placeholder. The int.TryParse guard at :116 refuses it; React's equivalent is the choice === '' guard.
    [Fact]
    public void Choosing_the_empty_placeholder_of_a_not_offered_kind_emits_nothing_and_a_real_choice_then_replaces_the_placeholder()
    {
        var changes = new List<RecordsDraftChange>();
        var cut = Editor(DraftOf("9007199254740993", Field("gauge", """{"kind_id":"legacy","version":"0.9.0","parameters":{"scale":2},"future_kind_member":"kept"}""")), Registry, changes);
        KindSelect(cut, 1).Change("");
        Assert.Empty(changes); Assert.Equal("0", Generation(cut));
        Assert.Equal([("", "legacy 0.9.0 (not offered)"), .. RegistryOptions], OptionsOf(cut, 1)); Assert.Equal("", Selected(cut, 1));

        KindSelect(cut, 1).Change("3");
        AssertDraft(Assert.Single(changes), "9007199254740993", 1, BodyOf(Field("gauge", """{"kind_id":"date","version":"2.0.0","parameters":{"scale":2},"future_kind_member":"kept"}""")));
        Assert.Equal(RegistryOptions, OptionsOf(cut, 1)); Assert.Equal("3", Selected(cut, 1)); Assert.Equal("1", Generation(cut));
    }

    // "" is the placeholder value; "-1" and "4" are outside the index range. None may bind KindOptions[0] (count 1.0.0) or anything else.
    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("4")]
    public void Choosing_an_empty_or_out_of_range_value_on_an_offered_kind_emits_nothing(string choice)
    {
        var changes = new List<RecordsDraftChange>();
        var cut = Editor(DraftOf("9007199254740993", Field("gauge", """{"kind_id":"date","version":"2.0.0","parameters":{}}""")), Registry, changes);
        Assert.Equal("3", Selected(cut, 1));
        KindSelect(cut, 1).Change(choice);
        Assert.Empty(changes); Assert.Equal("0", Generation(cut));
        Assert.Equal("3", Selected(cut, 1)); Assert.Equal(RegistryOptions, OptionsOf(cut, 1));
    }

    // Gap 3: KindOptions changing between renders. Options are positional (:29), the selection is re-derived each render (:22), and the
    // choice indexes the KindOptions current when it is handled (:117). A new list is not an edit: no emission, no generation change.
    [Fact]
    public void A_reordered_kind_options_list_reselects_by_kind_and_version_without_emitting_and_the_next_choice_indexes_the_new_list()
    {
        var changes = new List<RecordsDraftChange>();
        var cut = Editor(DraftOf("9007199254740993", Field("gauge", """{"kind_id":"date","version":"2.0.0","parameters":{}}"""), Field("quantity", """{"kind_id":"count","version":"1.0.0","parameters":{}}""")), Registry, changes, ProbeAt("9007199254740993", 0));
        Assert.Equal("3", Selected(cut, 1)); Assert.Equal("0", Selected(cut, 2)); Assert.Equal([ProbeText], Rendered(cut));

        IReadOnlyList<RecordsKindOption> reordered = [new("date", "2.0.0"), new("count", "1.0.0")];
        cut.Render(parameters => parameters.Add(component => component.KindOptions, reordered));
        Assert.Equal([("0", "date 2.0.0"), ("1", "count 1.0.0")], OptionsOf(cut, 1)); Assert.Equal("0", Selected(cut, 1)); Assert.Equal("1", Selected(cut, 2));
        Assert.Empty(changes); Assert.Equal("0", Generation(cut)); Assert.Equal([ProbeText], Rendered(cut));

        // "0" is date 2.0.0 in the new list; in the original registry it would have been count 1.0.0.
        KindSelect(cut, 2).Change("0");
        AssertDraft(Assert.Single(changes), "9007199254740993", 1, BodyOf(Field("gauge", """{"kind_id":"date","version":"2.0.0","parameters":{}}"""), Field("quantity", """{"kind_id":"date","version":"2.0.0","parameters":{}}""")));
        Assert.Equal("0", Selected(cut, 2)); Assert.Equal("1", Generation(cut));
        // The generation-0 diagnostics are now stale (:94).
        Assert.Empty(Rendered(cut));
    }

    [Fact]
    public void Kind_options_that_drop_the_current_kind_version_show_it_as_not_offered_without_emitting_and_restoring_them_reselects_it()
    {
        var changes = new List<RecordsDraftChange>();
        var cut = Editor(DraftOf("9007199254740993", Field("gauge", """{"kind_id":"date","version":"2.0.0","parameters":{"format":"iso"}}""")), Registry, changes, ProbeAt("9007199254740993", 0));
        IReadOnlyList<RecordsKindOption> withoutDate2 = [new("count", "1.0.0"), new("text", "1.0.0"), new("date", "1.0.0")];
        cut.Render(parameters => parameters.Add(component => component.KindOptions, withoutDate2));
        Assert.Equal([("", "date 2.0.0 (not offered)"), ("0", "count 1.0.0"), ("1", "text 1.0.0"), ("2", "date 1.0.0")], OptionsOf(cut, 1)); Assert.Equal("", Selected(cut, 1));
        Assert.Empty(changes); Assert.Equal("0", Generation(cut)); Assert.Equal([ProbeText], Rendered(cut));

        IReadOnlyList<RecordsKindOption> none = [];
        cut.Render(parameters => parameters.Add(component => component.KindOptions, none));
        Assert.Equal([("", "date 2.0.0 (not offered)")], OptionsOf(cut, 1)); Assert.Equal("", Selected(cut, 1));
        Assert.Equal("iso", Input(cut, "Field 1 parameter format").GetAttribute("value"));

        cut.Render(parameters => parameters.Add(component => component.KindOptions, Registry));
        Assert.Equal(RegistryOptions, OptionsOf(cut, 1)); Assert.Equal("3", Selected(cut, 1));
        Assert.Empty(changes); Assert.Equal("0", Generation(cut)); Assert.Equal([ProbeText], Rendered(cut));
    }

    // Gap 4: non-empty parameters survive a selector change (:118 writes only kind_id and version into a copy). The revision rides along
    // byte-exactly, null stays null, and the host's body is never mutated (:107).
    [Theory]
    [InlineData("9007199254740993", "9007199254740993")]
    [InlineData("9223372036854775807", "9223372036854775807")]
    [InlineData(null, "None")]
    public void Non_empty_parameters_and_unknown_kind_members_survive_a_selector_change(string? revision, string shown)
    {
        var host = DraftOf(revision, Field("gauge", """{"kind_id":"text","version":"1.0.0","parameters":{"max_length":120,"unit":"mm","nested":{"keep":[1,"two"]}},"future_kind_member":"kept"}"""), Field("quantity", """{"kind_id":"count","version":"1.0.0","parameters":{}}"""));
        var changes = new List<RecordsDraftChange>(); var cut = Editor(host, Registry, changes);
        Assert.Equal("120", Input(cut, "Field 1 parameter max_length").GetAttribute("value")); Assert.Equal("number", Input(cut, "Field 1 parameter max_length").GetAttribute("type"));
        Assert.Equal("mm", Input(cut, "Field 1 parameter unit").GetAttribute("value")); Assert.Equal("text", Input(cut, "Field 1 parameter unit").GetAttribute("type"));
        Assert.Empty(cut.FindAll("input[aria-label='Field 1 parameter nested']"));

        KindSelect(cut, 1).Change("2");
        AssertDraft(Assert.Single(changes), revision, 1, BodyOf(Field("gauge", """{"kind_id":"date","version":"1.0.0","parameters":{"max_length":120,"unit":"mm","nested":{"keep":[1,"two"]}},"future_kind_member":"kept"}"""), Field("quantity", """{"kind_id":"count","version":"1.0.0","parameters":{}}""")));
        Assert.Equal("120", Input(cut, "Field 1 parameter max_length").GetAttribute("value")); Assert.Equal("mm", Input(cut, "Field 1 parameter unit").GetAttribute("value")); Assert.Equal("2", Selected(cut, 1));
        Assert.Equal(shown, Revision(cut)); Assert.Equal("1", Generation(cut));
        Assert.True(JsonNode.DeepEquals(BodyOf(Field("gauge", """{"kind_id":"text","version":"1.0.0","parameters":{"max_length":120,"unit":"mm","nested":{"keep":[1,"two"]}},"future_kind_member":"kept"}"""), Field("quantity", """{"kind_id":"count","version":"1.0.0","parameters":{}}""")), host.Body), host.Body.ToJsonString());

        // A later parameter edit keeps the chosen kind/version.
        Input(cut, "Field 1 parameter unit").Change("cm");
        Assert.Equal(2, changes.Count); Assert.Equal((2, revision), (changes[1].Generation, changes[1].Draft.ExpectedRevision));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"kind_id":"date","version":"1.0.0","parameters":{"max_length":120,"unit":"cm","nested":{"keep":[1,"two"]}},"future_kind_member":"kept"}"""), changes[1].Draft.Body["fields"]![0]!["binding"]!["kind"]), changes[1].Draft.Body.ToJsonString());
    }

    // Generation separation and stale-result handling around a selector change (:81-89, :94, :105, :134-141). Local state only; nothing is written.
    [Fact]
    public void A_selector_change_is_a_local_edit_the_generation_advances_earlier_diagnostics_go_stale_an_echo_is_no_reset_and_a_new_host_revision_waits()
    {
        var changes = new List<RecordsDraftChange>();
        var cut = Editor(DraftOf("9007199254740993", Field("gauge", """{"kind_id":"text","version":"1.0.0","parameters":{"unit":"mm"}}""")), Registry, changes, ProbeAt("9007199254740993", 0));
        Assert.Equal([ProbeText], Rendered(cut));

        KindSelect(cut, 1).Change("2");
        AssertDraft(Assert.Single(changes), "9007199254740993", 1, BodyOf(Field("gauge", """{"kind_id":"date","version":"1.0.0","parameters":{"unit":"mm"}}""")));
        Assert.Equal("1", Generation(cut)); Assert.Empty(Rendered(cut));

        // The host echoes the edited draft: same identity, version and revision, so it is not a replacement.
        cut.Render(parameters => parameters.Add(component => component.Value, changes[0].Draft).Add(component => component.Diagnostics, ProbeAt("9007199254740993", 0)));
        Assert.Empty(cut.FindAll("[role='alert']")); Assert.Equal("2", Selected(cut, 1)); Assert.Equal("1", Generation(cut)); Assert.Empty(Rendered(cut));
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, ProbeAt("9007199254740993", 1)));
        Assert.Equal([ProbeText], Rendered(cut)); Assert.Single(changes);

        // A different host revision arrives while the selection is a local edit: it waits behind Keep/Discard.
        cut.Render(parameters => parameters.Add(component => component.Value, DraftOf("9007199254740994", Field("gauge", """{"kind_id":"count","version":"1.0.0","parameters":{"unit":"mm"}}"""))));
        Assert.Single(cut.FindAll("[role='alert']")); Assert.Equal("2", Selected(cut, 1)); Assert.Equal("9007199254740993", Revision(cut)); Assert.Equal("1", Generation(cut)); Assert.Single(changes);
        Button(cut, "Keep edits").Click();
        Assert.Empty(cut.FindAll("[role='alert']")); Assert.Equal("2", Selected(cut, 1)); Assert.Equal("9007199254740993", Revision(cut)); Assert.Single(changes);

        KindSelect(cut, 1).Change("3");
        Assert.Equal(2, changes.Count);
        AssertDraft(changes[1], "9007199254740993", 2, BodyOf(Field("gauge", """{"kind_id":"date","version":"2.0.0","parameters":{"unit":"mm"}}""")));

        var later = DraftOf("9007199254740995", Field("gauge", """{"kind_id":"count","version":"1.0.0","parameters":{"unit":"mm"}}"""));
        cut.Render(parameters => parameters.Add(component => component.Value, later));
        Button(cut, "Discard edits").Click();
        Assert.Equal(3, changes.Count);
        Assert.Equal((later, 3), (changes[2].Draft, changes[2].Generation));
        AssertDraft(changes[2], "9007199254740995", 3, BodyOf(Field("gauge", """{"kind_id":"count","version":"1.0.0","parameters":{"unit":"mm"}}""")));
        Assert.Equal("0", Selected(cut, 1)); Assert.Equal("9007199254740995", Revision(cut)); Assert.Equal("3", Generation(cut));
    }
}
