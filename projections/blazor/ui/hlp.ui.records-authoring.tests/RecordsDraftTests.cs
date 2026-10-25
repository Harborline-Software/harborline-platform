using System.Text.Json;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.RecordsAuthoring;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// T618 local Record Type draft editing only. Fixtures are the four native copies the test project links into records-draft-fixtures.
// Nothing here proves producer admission, canonical serialization or refusal ordering; those stay producer-only.
public sealed class RecordsDraftTests : BunitContext
{
    private static JsonNode Fixture(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "records-draft-fixtures", name)))!;
    private static readonly JsonNode Grammar = Fixture("records-current-grammar.json");
    private static readonly JsonArray RefusalCases = Fixture("records-refusal-cases.json")["cases"]!.AsArray();
    private static readonly JsonArray ParityCases = Fixture("records-parity-inputs.json")["cases"]!.AsArray();
    private static JsonNode G1 => Grammar["cases"]![0]!;
    private static readonly IReadOnlyList<RecordsKindOption> KindOptions = [.. Grammar["grammarFacts"]!["testHostKindRegistry"]!["kinds"]!.AsArray().Select(kind => new RecordsKindOption(kind!["kind_id"]!.GetValue<string>(), kind["version"]!.GetValue<string>()))];
    private static JsonNode RefusalCase(string id) => RefusalCases.Single(item => item!["id"]!.GetValue<string>() == id)!;
    private static JsonNode ParityCase(string id) => ParityCases.Single(item => item!["id"]!.GetValue<string>() == id)!;
    // The frozen producer fixtures carry small JSON-number revisions (1 and 2). The editor takes the host's canonical decimal string, so each
    // fixture number maps to its string through this literal table, never through ToString or arithmetic.
    private static string FixtureRevision(JsonNode? revision) => revision!.GetValue<long>() switch { 1 => "1", 2 => "2", _ => throw new InvalidOperationException("unmapped fixture revision") };
    private static RecordsDraft G1Draft() => new(G1["catalogue"]!["definitionId"]!.GetValue<string>(), G1["catalogue"]!["definitionVersion"]!.GetValue<string>(), FixtureRevision(G1["catalogue"]!["expectedCreateRevision"]), "Records", G1["expectedStoredBody"]!.DeepClone().AsObject());
    private static IReadOnlyList<RecordsDiagnostic> Diagnostics(JsonNode? list) => [.. list!.AsArray().Select(item => new RecordsDiagnostic(item!["code"]!.GetValue<string>(), item["pointer"]!.GetValue<string>()))];
    private static RecordsDiagnosticSet Supplied(RecordsDraft draft, int generation, IReadOnlyList<RecordsDiagnostic> diagnostics) => new(draft.Identity, draft.ExpectedRevision, generation, diagnostics);

    // Fixture expansion for display only: the case's members in a not-yet-identified draft. It is not a producer request.
    private static RecordsDraft CaseDraft(JsonNode item)
    {
        if (item["rawSave"] is { } raw) return new(raw["key"]!["definitionId"]!.GetValue<string>(), raw["versionId"]!.GetValue<string>(), FixtureRevision(raw["expectedRevision"]), "Records", JsonNode.Parse(raw["bodyJson"]!.GetValue<string>())!.AsObject());
        var request = (item["createRequest"] ?? G1["createRequest"]!["newRecordType"])!.DeepClone().AsObject();
        if (item["replace"] is JsonObject replace) foreach (var member in replace) request[member.Key] = member.Value?.DeepClone();
        return new("", request["version"]!.GetValue<string>(), null, "Records", new JsonObject { ["name"] = request["name"]!.DeepClone(), ["envelope"] = new JsonObject { ["tenant"] = request["tenant"]!.DeepClone(), ["section"] = request["section"]!.DeepClone() }, ["fields"] = request["fields"]!.DeepClone() });
    }

    private IRenderedComponent<HarborlineRecordTypeDraftEditor> Editor(RecordsDraft value, List<RecordsDraftChange> changes, RecordsDiagnosticSet? diagnostics = null, bool readOnly = false)
        => Render<HarborlineRecordTypeDraftEditor>(parameters => parameters.Add(component => component.Value, value).Add(component => component.KindOptions, KindOptions).Add(component => component.Diagnostics, diagnostics).Add(component => component.ReadOnly, readOnly).Add(component => component.DraftChanged, (RecordsDraftChange change) => changes.Add(change)));
    private static (string?, string?, string)[] Rendered(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut) => [.. cut.FindAll("ul[aria-label='Diagnostics'] li").Select(item => (item.GetAttribute("data-code"), item.GetAttribute("data-pointer"), item.TextContent))];
    private static (string?, string?, string)[] Expected(IReadOnlyList<RecordsDiagnostic> diagnostics) => [.. diagnostics.Select(item => ((string?)item.Code, (string?)item.Pointer, $"{item.Code} at {item.Pointer}"))];
    private static IElement Button(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, string text) => cut.FindAll("button").Single(button => button.TextContent.Trim() == text);
    private static string? Generation(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut) => cut.Find("form").GetAttribute("data-generation");
    private static string Revision(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut) => cut.Find("output[aria-label='Expected revision']").TextContent;
    private static string? NameValue(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut) => cut.Find("input[aria-label='Record type name']").GetAttribute("value");

    [Fact]
    public void G1_local_name_and_field_edits_preserve_every_untouched_member_of_the_stored_body()
    {
        var changes = new List<RecordsDraftChange>();
        var cut = Editor(G1Draft(), changes);
        Assert.Equal(G1["catalogue"]!["definitionId"]!.GetValue<string>(), cut.Find("output[aria-label='Record type identity']").TextContent);
        Assert.Equal("1", Revision(cut));
        cut.Find("input[aria-label='Record type name']").Change("Asset Class local");
        cut.Find("input[aria-label='Field 3 display name']").Change("Remarks");
        var expected = G1["expectedStoredBody"]!.DeepClone().AsObject();
        expected["name"] = "Asset Class local";
        expected["fields"]![2]!["display_name"] = "Remarks";
        Assert.True(JsonNode.DeepEquals(expected, changes[^1].Draft.Body), changes[^1].Draft.Body.ToJsonString());
        Assert.Equal((G1Draft().Identity, G1Draft().VersionId, G1Draft().ExpectedRevision), (changes[^1].Draft.Identity, changes[^1].Draft.VersionId, changes[^1].Draft.ExpectedRevision));
        Assert.True(JsonNode.DeepEquals(G1["expectedStoredBody"], G1Draft().Body));
        // The existing Boolean floor is carried, never offered as a control: the only checkboxes are the two revision 3 header ones.
        Assert.Equal(["Amendments require a reason", "Keep history"], cut.FindAll("input[type='checkbox']").Select(box => box.GetAttribute("aria-label") ?? "").Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Preserves_unknown_members_and_other_reference_members_while_editing_reference_inputs()
    {
        var body = G1Draft().Body;
        var field = RefusalCase("R5-reference-binding-conflict")["createRequest"]!["fields"]![0]!.DeepClone().AsObject();
        field["future_field_member"] = "kept"; field["reference"]!["required_trait_id"] = "trait.kept"; field["reference"]!["pin"] = "kept";
        body["fields"] = new JsonArray(field); body["future_member"] = new JsonObject { ["kept"] = new JsonArray(1, "two") }; body["envelope"]!["future_envelope_member"] = true;
        var changes = new List<RecordsDraftChange>();
        var cut = Editor(G1Draft() with { Body = body }, changes);
        cut.Find("input[aria-label='Field 1 reference target type']").Change("eam.space");
        cut.Find("select[aria-label='Field 1 reference on delete']").Change("orphan");
        var expected = body.DeepClone().AsObject();
        expected["fields"]![0]!["reference"]!["target_type_id"] = "eam.space"; expected["fields"]![0]!["reference"]!["on_delete"] = "orphan";
        Assert.True(JsonNode.DeepEquals(expected, changes[^1].Draft.Body), changes[^1].Draft.Body.ToJsonString());
    }

    [Fact]
    public void Asks_for_the_definition_kind_before_the_name_and_adds_a_field_without_invented_members()
    {
        var changes = new List<RecordsDraftChange>();
        var cut = Editor(RecordsDraft.Empty, changes);
        Assert.Empty(cut.FindAll("input[aria-label='Record type name']"));
        Assert.Equal<string?>((IEnumerable<string?>)new[] { "", "Records" }, cut.FindAll("select[aria-label='Definition kind'] option").Select(option => option.GetAttribute("value")).ToArray());
        cut.Find("select[aria-label='Definition kind']").Change("Records");
        Assert.Equal(("", "", (string?)null, (string?)"Records", 0),(changes[^1].Draft.Identity, changes[^1].Draft.VersionId, changes[^1].Draft.ExpectedRevision, changes[^1].Draft.DefinitionKind, changes[^1].Draft.Body.Count));
        cut.Find("input[aria-label='Record type name']").Change("Asset Class");
        Button(cut, "Add field").Click();
        cut.Find("input[aria-label='Field 1 key']").Change("notes");
        cut.Find("input[aria-label='Field 1 display name']").Change("Notes");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"name":"Asset Class","fields":[{"field_key":"notes","display_name":"Notes"}]}"""), changes[^1].Draft.Body), changes[^1].Draft.Body.ToJsonString());
        Assert.Empty(cut.FindAll("select[aria-label='Field 1 kind']"));
    }

    [Fact]
    public void P1_inputs_are_all_refused_source_cases()
    {
        var refused = RefusalCases.Where(item => item!["expected"]!["outcome"]!.GetValue<string>() == "refused").Select(item => item!["id"]!.GetValue<string>()).ToArray();
        Assert.All(ParityCase("P1-refusal-rendering-parity")["inputs"]!.AsArray().Select(input => input!.GetValue<string>()), id => Assert.Contains(id, refused));
    }

    [Theory]
    [InlineData("R1-punctuation-name")]
    [InlineData("R2-zero-revision-collision")]
    [InlineData("R3-duplicate-field-key")]
    [InlineData("R4-duplicate-field-key-raw-complete-body")]
    [InlineData("R5-reference-binding-conflict")]
    [InlineData("R6-unknown-kind")]
    public void Renders_each_supplied_ordered_refusal_list_exactly_as_given(string caseId)
    {
        var item = RefusalCase(caseId); var draft = CaseDraft(item); var refusals = Diagnostics(item["expected"]!["refusals"]); var changes = new List<RecordsDraftChange>();
        var cut = Editor(draft, changes, Supplied(draft, 0, refusals));
        Assert.Equal(Expected(refusals), Rendered(cut));
        Assert.Empty(changes);
    }

    [Fact]
    public void R7_a_local_section_edit_never_mints_an_identity_and_the_host_supplied_identity_is_shown_as_given()
    {
        var item = RefusalCase("R7-same-name-other-section-positive"); var baseCase = item.DeepClone().AsObject(); baseCase.Remove("replace");
        var section = item["replace"]!["section"]!.GetValue<string>(); var minted = item["expected"]!["mintedRecordTypeId"]!.GetValue<string>();
        var changes = new List<RecordsDraftChange>();
        var cut = Editor(CaseDraft(baseCase), changes);
        cut.Find("input[aria-label='Section']").Change(section);
        Assert.Equal(section, changes[^1].Draft.Body["envelope"]!["section"]!.GetValue<string>());
        Assert.Equal("", changes[^1].Draft.Identity);
        Assert.Equal("Not yet assigned", cut.Find("output[aria-label='Record type identity']").TextContent);
        cut.Render(parameters => parameters.Add(component => component.Value, changes[^1].Draft with { Identity = minted, ExpectedRevision = "1" }));
        Button(cut, "Discard edits").Click();
        Assert.Equal(minted, cut.Find("output[aria-label='Record type identity']").TextContent);
    }

    [Fact]
    public void P2_clean_incoming_replaces_a_parent_echo_is_not_a_reset_dirty_incoming_waits_and_stale_generations_stay_ignored()
    {
        var item = ParityCase("P2-identity-revision-reset"); var steps = item["inputSequence"]!.AsArray(); var staleSave = Diagnostics(item["producerFact"]!["staleSave"]);
        var loaded = G1Draft() with { Identity = steps[0]!["identity"]!.GetValue<string>(), VersionId = steps[0]!["versionId"]!.GetValue<string>(), ExpectedRevision = FixtureRevision(steps[0]!["expectedRevision"]) };
        var changes = new List<RecordsDraftChange>();
        var cut = Editor(loaded, changes);
        Assert.Equal("0", Generation(cut));

        var externalBody = loaded.Body.DeepClone().AsObject(); externalBody["name"] = "Asset Class external";
        var external = loaded with { ExpectedRevision = FixtureRevision(steps[1]!["expectedRevision"]), Body = externalBody };
        cut.Render(parameters => parameters.Add(component => component.Value, external));
        Assert.Empty(cut.FindAll("[role='alert']")); Assert.Equal("Asset Class external", cut.Find("input[aria-label='Record type name']").GetAttribute("value")); Assert.Equal("2", Revision(cut)); Assert.Equal("1", Generation(cut));

        cut.Render(parameters => parameters.Add(component => component.Diagnostics, Supplied(loaded, 0, staleSave)));
        Assert.Empty(Rendered(cut));
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, Supplied(external, 1, staleSave)));
        Assert.Equal(Expected(staleSave), Rendered(cut));

        cut.Find("input[aria-label='Record type name']").Change("local");
        Assert.Equal(2, changes[^1].Generation);
        Assert.Empty(Rendered(cut));
        cut.Render(parameters => parameters.Add(component => component.Value, changes[^1].Draft));
        Assert.Empty(cut.FindAll("[role='alert']")); Assert.Equal("local", cut.Find("input[aria-label='Record type name']").GetAttribute("value")); Assert.Equal("2", Generation(cut)); Assert.Empty(Rendered(cut));

        var otherBody = G1Draft().Body; otherBody["envelope"]!["section"] = "finance";
        var other = new RecordsDraft(steps[2]!["identity"]!.GetValue<string>(), steps[2]!["versionId"]!.GetValue<string>(), FixtureRevision(steps[2]!["expectedRevision"]), "Records", otherBody);
        cut.Render(parameters => parameters.Add(component => component.Value, other));
        Assert.Single(cut.FindAll("[role='alert']"));
        Button(cut, "Keep edits").Click();
        Assert.Empty(cut.FindAll("[role='alert']")); Assert.Equal("local", cut.Find("input[aria-label='Record type name']").GetAttribute("value")); Assert.Equal(loaded.Identity, cut.Find("output[aria-label='Record type identity']").TextContent);

        // The next host revision is supplied as a literal; the editor never increments a revision.
        var otherRevised = other with { ExpectedRevision = "2" };
        cut.Render(parameters => parameters.Add(component => component.Value, otherRevised));
        Button(cut, "Discard edits").Click();
        Assert.Equal((otherRevised, 3), (changes[^1].Draft, changes[^1].Generation));
        Assert.Equal(other.Identity, cut.Find("output[aria-label='Record type identity']").TextContent); Assert.Equal("finance", cut.Find("input[aria-label='Section']").GetAttribute("value"));
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, Supplied(otherRevised, 2, staleSave)));
        Assert.Empty(Rendered(cut));
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, Supplied(otherRevised, 3, staleSave)));
        Assert.Equal(Expected(staleSave), Rendered(cut));
    }

    [Fact]
    public void P3_a_kind_version_change_is_a_local_edit_with_no_clearing_and_the_supplied_retention_refusal_is_rendered_as_given()
    {
        var item = ParityCase("P3-kind-version-change");
        // The P3 base input, from its fixture description: G1 fields plus acquired_on bound to date 1.0.0 and named as the retention clock.
        var acquiredOn = JsonNode.Parse("""{"field_key":"acquired_on","display_name":"Acquired on","binding":{"kind":{"kind_id":"date","version":"1.0.0","parameters":{}},"constraints":{"required":true,"minimum_count":0,"maximum_count":1,"read_role_ids":[]}}}""")!;
        var body = G1Draft().Body; body["fields"]!.AsArray().Add(acquiredOn); body["retention_clock_field_id"] = "acquired_on";
        var changes = new List<RecordsDraftChange>();
        var cut = Editor(G1Draft() with { Body = body }, changes);
        Assert.Equal("acquired_on", cut.Find("select[aria-label='Retention clock field']").GetAttribute("value"));
        var options = cut.FindAll("select[aria-label='Field 4 kind'] option");
        Assert.Equal(cut.Find("select[aria-label='Field 4 kind']").GetAttribute("value"), options.Single(option => option.TextContent == "date 1.0.0").GetAttribute("value"));
        cut.Find("select[aria-label='Field 4 kind']").Change(options.Single(option => option.TextContent == "date 2.0.0").GetAttribute("value"));
        var expected = body.DeepClone().AsObject(); expected["fields"]![3]!["binding"]!["kind"]!["version"] = "2.0.0";
        Assert.True(JsonNode.DeepEquals(expected, changes[^1].Draft.Body), changes[^1].Draft.Body.ToJsonString());
        Assert.Empty(Rendered(cut));
        var refusals = Diagnostics(item["expected"]!["changed"]!["refusals"]);
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, Supplied(changes[^1].Draft, changes[^1].Generation, refusals)));
        Assert.Equal(Expected(refusals), Rendered(cut));
    }

    [Fact]
    public void Read_only_also_blocks_pending_Keep_and_Discard_after_an_editable_draft_becomes_dirty()
    {
        var draft = G1Draft(); var changes = new List<RecordsDraftChange>(); var cut = Editor(draft, changes);
        cut.Find("input[aria-label='Record type name']").Change("local");
        Assert.Single(changes);
        cut.Render(parameters => parameters.Add(component => component.Value, draft with { ExpectedRevision = "2" }).Add(component => component.ReadOnly, true));
        var keep = Button(cut, "Keep edits"); var discard = Button(cut, "Discard edits");
        Assert.True(keep.HasAttribute("disabled")); Assert.True(discard.HasAttribute("disabled"));
        keep.Click(); discard.Click();
        Assert.Single(changes);
        Assert.Single(cut.FindAll("[role='alert']"));
        Assert.Equal("local", cut.Find("input[aria-label='Record type name']").GetAttribute("value"));
        Assert.Equal("1", Revision(cut));
        Assert.Equal("1", Generation(cut));
    }

    [Fact]
    public void Withdraws_a_pending_draft_when_the_host_returns_to_the_current_lifecycle_value()
    {
        var draft = G1Draft(); var changes = new List<RecordsDraftChange>(); var cut = Editor(draft, changes);
        cut.Find("input[aria-label='Record type name']").Change("local");
        cut.Render(parameters => parameters.Add(component => component.Value, draft with { ExpectedRevision = "2" }));
        Assert.Single(cut.FindAll("[role='alert']"));
        cut.Render(parameters => parameters.Add(component => component.Value, changes[0].Draft));
        Assert.Empty(cut.FindAll("[role='alert']"));
        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Trim() == "Discard edits");
        Assert.Equal("local", cut.Find("input[aria-label='Record type name']").GetAttribute("value"));
        Assert.Single(changes);
    }

    [Fact]
    public void Does_not_replace_a_numeric_JSON_parameter_with_text_when_its_input_is_cleared()
    {
        var draft = G1Draft(); var body = draft.Body.DeepClone().AsObject();
        // Synthetic local JSON value; this asserts editing type preservation, not producer parameter admission.
        body["fields"]![0]!["binding"]!["kind"]!["parameters"] = new JsonObject { ["probe"] = 2 };
        var changes = new List<RecordsDraftChange>(); var cut = Editor(draft with { Body = body }, changes);
        cut.Find("input[aria-label='Field 1 parameter probe']").Change("");
        Assert.Empty(changes);
        cut.Find("input[aria-label='Field 1 parameter probe']").Change("3");
        Assert.Single(changes);
        Assert.Equal(3m, changes[0].Draft.Body["fields"]![0]!["binding"]!["kind"]!["parameters"]!["probe"]!.GetValue<decimal>());
        Assert.Equal(2, body["fields"]![0]!["binding"]!["kind"]!["parameters"]!["probe"]!.GetValue<int>());
    }

    [Fact]
    public void P4_read_only_disables_every_control_emits_nothing_and_keeps_the_supplied_list_exactly()
    {
        var draft = G1Draft(); var list = Diagnostics(RefusalCase("R1-punctuation-name")["expected"]!["refusals"]); var changes = new List<RecordsDraftChange>();
        var cut = Editor(draft, changes, Supplied(draft, 0, list), readOnly: true);
        Assert.NotNull(cut.Find("form").GetAttribute("data-read-only"));
        var all = cut.FindAll("input, select, button").Count;
        Assert.True(all > 0);
        Assert.Equal(all, cut.FindAll("fieldset.hl-records-authoring__read-only[disabled] input, fieldset.hl-records-authoring__read-only[disabled] select, fieldset.hl-records-authoring__read-only[disabled] button").Count);
        cut.Find("input[aria-label='Record type name']").Change("changed");
        cut.Find("select[aria-label='Field 1 kind']").Change("0");
        Button(cut, "Add field").Click();
        Button(cut, "Remove field 1").Click();
        Assert.Empty(changes);
        Assert.Equal(G1["expectedStoredBody"]!["name"]!.GetValue<string>(), cut.Find("input[aria-label='Record type name']").GetAttribute("value"));
        Assert.Equal(Expected(list), Rendered(cut));
        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("Save", StringComparison.OrdinalIgnoreCase) || button.TextContent.Contains("Publish", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Goal6 lossless revision transport ------------------------------------------------------------------------------------
    // Every expected revision is a literal: the host contract's decimal string (RecordsDraftHostContract.cs:56-57,72-99).
    // Method names pair one-for-one with RecordTypeDraftEditor.test.tsx; the pairing table is in the Goal6 HANDOFF.md.
    private const string RejectedRevisionNote = "A host draft was not loaded because its revision is not a canonical decimal string.";
    private static readonly IReadOnlyList<RecordsDiagnostic> ProbeDiagnostics = [new("definition.revision_conflict", "/expectedRevision")];
    private static RecordsDiagnosticSet DiagnosticsAt(string revision, int generation) => new(G1Draft().Identity, revision, generation, ProbeDiagnostics);

    [Theory]
    [InlineData("0")]
    [InlineData("9007199254740992")]
    [InlineData("9007199254740993")]
    [InlineData("9223372036854775807")]
    public void Revision_survives_load_clean_replacement_diagnostic_matching_and_the_edit_callback_unchanged(string revision)
    {
        var loaded = G1Draft() with { ExpectedRevision = revision }; var changes = new List<RecordsDraftChange>();
        var cut = Editor(loaded, changes, DiagnosticsAt(revision, 0));
        Assert.Equal(revision, Revision(cut)); Assert.Equal(Expected(ProbeDiagnostics), Rendered(cut));

        var replaced = loaded with { VersionId = "replacement-version" };
        cut.Render(parameters => parameters.Add(component => component.Value, replaced).Add(component => component.Diagnostics, DiagnosticsAt(revision, 1)));
        Assert.Equal("1", Generation(cut)); Assert.Equal(revision, Revision(cut)); Assert.Equal(Expected(ProbeDiagnostics), Rendered(cut)); Assert.Empty(changes);

        cut.Find("input[aria-label='Record type name']").Change("local");
        Assert.Equal((loaded.Identity, "replacement-version", (string?)revision, 2), (changes[^1].Draft.Identity, changes[^1].Draft.VersionId, changes[^1].Draft.ExpectedRevision, changes[^1].Generation));
        Assert.Equal("local", changes[^1].Draft.Body["name"]!.GetValue<string>());
        cut.Render(parameters => parameters.Add(component => component.Value, changes[^1].Draft).Add(component => component.Diagnostics, DiagnosticsAt(revision, 2)));
        Assert.Equal(Expected(ProbeDiagnostics), Rendered(cut)); Assert.Equal(revision, Revision(cut)); Assert.Single(changes);
    }

    [Fact]
    public void A_held_incoming_revision_survives_Keep_and_Discard_byte_exactly()
    {
        var loaded = G1Draft() with { ExpectedRevision = "0" }; var changes = new List<RecordsDraftChange>(); var cut = Editor(loaded, changes);
        cut.Find("input[aria-label='Record type name']").Change("local");
        cut.Render(parameters => parameters.Add(component => component.Value, loaded with { ExpectedRevision = "9007199254740993" }));
        Button(cut, "Keep edits").Click();
        Assert.Empty(cut.FindAll("[role='alert']")); Assert.Equal("0", Revision(cut));
        cut.Find("input[aria-label='Record type name']").Change("local again");
        Assert.Equal(((string?)"0", 2), (changes[^1].Draft.ExpectedRevision, changes[^1].Generation));

        var discardedBody = loaded.Body.DeepClone().AsObject(); discardedBody["name"] = "host";
        var discarded = loaded with { ExpectedRevision = "9223372036854775807", Body = discardedBody };
        cut.Render(parameters => parameters.Add(component => component.Value, discarded));
        Button(cut, "Discard edits").Click();
        Assert.Equal((discarded, 3), (changes[^1].Draft, changes[^1].Generation));
        Assert.Equal("9223372036854775807", changes[^1].Draft.ExpectedRevision); Assert.Equal("9223372036854775807", Revision(cut));
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, DiagnosticsAt("9223372036854775807", 3)));
        Assert.Equal(Expected(ProbeDiagnostics), Rendered(cut));
    }

    [Fact]
    public void Treats_9007199254740992_and_9007199254740993_as_distinct_revisions()
    {
        // Both are the same IEEE-754 double, so a double-based key or comparison would conflate them.
        var changes = new List<RecordsDraftChange>(); var upper = G1Draft() with { ExpectedRevision = "9007199254740993" };
        var cut = Editor(upper, changes, DiagnosticsAt("9007199254740992", 0));
        Assert.Empty(Rendered(cut));
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, DiagnosticsAt("9007199254740993", 0)));
        Assert.Equal(Expected(ProbeDiagnostics), Rendered(cut));

        // A clean host value that differs only between these two revisions is a replacement, not an echo.
        var lower = upper with { ExpectedRevision = "9007199254740992" };
        cut.Render(parameters => parameters.Add(component => component.Value, lower).Add(component => component.Diagnostics, DiagnosticsAt("9007199254740993", 1)));
        Assert.Equal("1", Generation(cut)); Assert.Equal("9007199254740992", Revision(cut)); Assert.Empty(Rendered(cut));
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, DiagnosticsAt("9007199254740992", 1)));
        Assert.Equal(Expected(ProbeDiagnostics), Rendered(cut));
        Assert.Empty(changes);
    }

    [Fact]
    public void Advances_the_local_generation_on_every_edit_while_the_revision_stays_the_host_value()
    {
        var changes = new List<RecordsDraftChange>(); var cut = Editor(G1Draft() with { ExpectedRevision = "9007199254740993" }, changes);
        Assert.Equal("0", Generation(cut));
        cut.Find("input[aria-label='Record type name']").Change("a");
        cut.Find("input[aria-label='Record type name']").Change("b");
        cut.Find("input[aria-label='Section']").Change("c");
        Assert.Equal(new[] { (1, (string?)"9007199254740993"), (2, (string?)"9007199254740993"), (3, (string?)"9007199254740993") }, changes.Select(change => (change.Generation, change.Draft.ExpectedRevision)).ToArray());
        Assert.Equal("3", Generation(cut)); Assert.Equal("9007199254740993", Revision(cut));
    }

    // Each fails the host grammar: empty, leading zero, sign, negative zero, whitespace, decimal point, exponent, non-ASCII digits (BMP and the
    // surrogate pair U+1D7CF), and above Int64 (19 and 20 digits).
    [Theory]
    [InlineData("")]
    [InlineData("00")]
    [InlineData("01")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("-0")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1.0")]
    [InlineData("1e3")]
    [InlineData("\u0661")]
    [InlineData("\uFF11")]
    [InlineData("\U0001D7CF")]
    [InlineData("9223372036854775808")]
    [InlineData("9999999999999999999")]
    [InlineData("18446744073709551616")]
    public void Rejects_the_noncanonical_revision_on_initial_load_without_adopting_or_emitting(string revision)
    {
        var changes = new List<RecordsDraftChange>();
        var cut = Editor(G1Draft() with { ExpectedRevision = revision }, changes);
        Assert.Equal(RejectedRevisionNote, cut.Find("p[role='status']").TextContent);
        Assert.Empty(cut.FindAll("output[aria-label='Expected revision']")); Assert.Empty(cut.FindAll("input[aria-label='Record type name']")); Assert.Empty(cut.FindAll("[role='alert']"));
        Assert.Empty(changes);

        // The first acceptable host value then loads as the initial draft; nothing of the rejected value survives.
        var accepted = G1Draft() with { ExpectedRevision = "7" };
        cut.Render(parameters => parameters.Add(component => component.Value, accepted));
        Assert.Empty(cut.FindAll("p[role='status']")); Assert.Equal("7", Revision(cut)); Assert.Equal("0", Generation(cut)); Assert.Empty(changes);
        cut.Find("input[aria-label='Record type name']").Change("local");
        Assert.Single(changes);
        Assert.Equal((accepted.Identity, accepted.VersionId, (string?)"7", 1), (changes[0].Draft.Identity, changes[0].Draft.VersionId, changes[0].Draft.ExpectedRevision, changes[0].Generation));
        Assert.Equal("local", changes[0].Draft.Body["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("00")]
    [InlineData("01")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("-0")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1.0")]
    [InlineData("1e3")]
    [InlineData("\u0661")]
    [InlineData("\uFF11")]
    [InlineData("\U0001D7CF")]
    [InlineData("9223372036854775808")]
    [InlineData("9999999999999999999")]
    [InlineData("18446744073709551616")]
    public void Rejects_the_noncanonical_incoming_revision_without_replacing_holding_or_emitting(string revision)
    {
        var current = G1Draft() with { ExpectedRevision = "7" }; var changes = new List<RecordsDraftChange>(); var cut = Editor(current, changes);
        var rejectedBody = current.Body.DeepClone().AsObject(); rejectedBody["name"] = "rejected";
        var rejected = current with { Identity = "eam.rejected", ExpectedRevision = revision, Body = rejectedBody };
        cut.Render(parameters => parameters.Add(component => component.Value, rejected));
        Assert.Equal(RejectedRevisionNote, cut.Find("p[role='status']").TextContent); Assert.Empty(cut.FindAll("[role='alert']"));
        Assert.Equal(G1["expectedStoredBody"]!["name"]!.GetValue<string>(), NameValue(cut)); Assert.Equal("7", Revision(cut)); Assert.Equal("0", Generation(cut)); Assert.Empty(changes);

        cut.Find("input[aria-label='Record type name']").Change("local");
        Assert.Equal((current.Identity, (string?)"7", 1), (changes[^1].Draft.Identity, changes[^1].Draft.ExpectedRevision, changes[^1].Generation));
        // Dirty: a rejected value is not held behind Keep/Discard either.
        cut.Render(parameters => parameters.Add(component => component.Value, rejected with { VersionId = "rejected-again" }));
        Assert.Empty(cut.FindAll("[role='alert']")); Assert.Equal("local", NameValue(cut)); Assert.Equal("7", Revision(cut)); Assert.Equal("1", Generation(cut)); Assert.Single(changes);
        cut.Render(parameters => parameters.Add(component => component.Value, changes[0].Draft));
        Assert.Empty(cut.FindAll("p[role='status']")); Assert.Equal("local", NameValue(cut)); Assert.Single(changes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00")]
    [InlineData("01")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("-0")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1.0")]
    [InlineData("1e3")]
    [InlineData("\u0661")]
    [InlineData("\uFF11")]
    [InlineData("\U0001D7CF")]
    [InlineData("9223372036854775808")]
    [InlineData("9999999999999999999")]
    [InlineData("18446744073709551616")]
    public void Never_shows_diagnostics_whose_revision_is_noncanonical(string revision)
    {
        var current = G1Draft() with { ExpectedRevision = "7" }; var changes = new List<RecordsDraftChange>();
        var cut = Editor(current, changes, DiagnosticsAt(revision, 0));
        Assert.Empty(Rendered(cut));
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, DiagnosticsAt("7", 0)));
        Assert.Equal(Expected(ProbeDiagnostics), Rendered(cut));
        Assert.Empty(changes);
    }

    [Fact]
    public void Treats_a_null_revision_as_absent_and_carries_it_unchanged()
    {
        // The host wire is string? ExpectedRevision (RecordsDraftHostContract.cs:156): null means no revision. It is neither rejected nor defaulted.
        var draft = G1Draft() with { ExpectedRevision = null }; var changes = new List<RecordsDraftChange>();
        var cut = Editor(draft, changes, new RecordsDiagnosticSet(draft.Identity, null, 0, ProbeDiagnostics));
        Assert.Empty(cut.FindAll("p[role='status']")); Assert.Equal("None", Revision(cut)); Assert.Equal(Expected(ProbeDiagnostics), Rendered(cut));
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, DiagnosticsAt("0", 0)));
        Assert.Empty(Rendered(cut));
        cut.Find("input[aria-label='Record type name']").Change("local");
        Assert.Single(changes); Assert.Null(changes[0].Draft.ExpectedRevision); Assert.Equal(1, changes[0].Generation);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("9007199254740993")]
    public void Type_boundary_string_revision_refuses_a_JSON_number_during_deserialization(string number)
    {
        // A type-boundary/serializer pin, not an editor behavior test: the Blazor editor never deserializes JSON. It checks that the
        // string? revision on RecordsDraft and RecordsDiagnosticSet makes standard System.Text.Json binding refuse a number token, as host
        // test RecordsDraftHostContractTests.cs:104 does for the wire, so a JSON number can never reach the editor as text.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RecordsDraft>($$$"""{"identity":"eam.asset-class","versionId":"v1","expectedRevision":{{{number}}},"body":{}}""", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RecordsDiagnosticSet>($$$"""{"identity":"eam.asset-class","expectedRevision":{{{number}}},"generation":0,"diagnostics":[]}""", options));
        var draft = JsonSerializer.Deserialize<RecordsDraft>($$$"""{"identity":"eam.asset-class","versionId":"v1","expectedRevision":"{{{number}}}","body":{}}""", options)!;
        Assert.Equal(number, draft.ExpectedRevision);
    }

    // ---- Interface revision 2 (T-618 item 4, records-auth-42): a diagnostic's optional target and the separate warnings list ----
    // The literals are the T-618 preflight producer values; every expected row and text is a literal, never built from the input.
    // Method names pair one-for-one with RecordTypeDraftEditor.test.tsx ("Revision2: ...").
    private static readonly RecordsDiagnostic Undeclared = new("records.reference.dependency_undeclared", "/fields/0/reference") { Target = "eam-core/eam.meter@1.0.0" };
    private static readonly RecordsDiagnostic Unresolved = new("records.class.unresolved", "/class_id");
    private static readonly RecordsWarning RetentionUnset = new("records.retention.unset", "/retention_policy");
    // Synthetic second warning, only so warning order is observable: the producer emits one warning code, and this one sorts before it.
    private static readonly RecordsWarning AuditUnset = new("records.audit.unset", "/audit_policy");
    private static readonly (string?, string?, string?, string) UndeclaredRow = ("records.reference.dependency_undeclared", "/fields/0/reference", "eam-core/eam.meter@1.0.0", "records.reference.dependency_undeclared at /fields/0/reference for eam-core/eam.meter@1.0.0");
    private static readonly (string?, string?, string?, string) UnresolvedRow = ("records.class.unresolved", "/class_id", null, "records.class.unresolved at /class_id");
    private const string RetentionText = "records.retention.unset at /retention_policy";
    // The third member is null when the item carries no data-target attribute at all.
    private static (string?, string?, string?, string)[] DiagnosticRows(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut) => [.. cut.FindAll("[aria-label='Diagnostics'] li").Select(item => (item.GetAttribute("data-code"), item.GetAttribute("data-pointer"), item.GetAttribute("data-target"), item.TextContent))];
    private static string[] WarningTexts(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut) => [.. cut.FindAll("[aria-label='Warnings'] li").Select(item => item.TextContent)];
    private static bool HasDiagnosticsList(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut) => cut.FindAll("[aria-label='Diagnostics']").Count > 0;
    private static int NoteCount(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut) => cut.FindAll("p.hl-records-authoring__note").Count(note => note.TextContent == "Warnings do not block publication.");
    private static bool HasWarningsList(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut) => cut.FindAll("[aria-label='Warnings']").Count > 0;
    // A null warnings argument leaves the member at its default (absent).
    private static RecordsDiagnosticSet Preview(RecordsDraft draft, int generation, IReadOnlyList<RecordsDiagnostic> diagnostics, IReadOnlyList<RecordsWarning>? warnings = null)
        => warnings is null ? Supplied(draft, generation, diagnostics) : Supplied(draft, generation, diagnostics) with { Warnings = warnings };

    [Fact]
    public void Revision2_a_targeted_diagnostic_renders_its_target_after_the_pointer_and_as_data_target_in_order_beside_an_untargeted_revision1_one()
    {
        var draft = G1Draft(); var changes = new List<RecordsDraftChange>();
        var cut = Editor(draft, changes, Preview(draft, 0, [Undeclared, Unresolved]));
        Assert.Equal([UndeclaredRow, UnresolvedRow], DiagnosticRows(cut));
        Assert.False(cut.FindAll("[aria-label='Diagnostics'] li")[1].HasAttribute("data-target"));
        Assert.Empty(changes);
    }

    [Fact]
    public void Revision2_an_empty_string_or_null_target_renders_exactly_like_no_target()
    {
        var draft = G1Draft(); var changes = new List<RecordsDraftChange>();
        var cut = Editor(draft, changes, Preview(draft, 0, [new("records.reference.dependency_undeclared", "/fields/0/reference") { Target = "" }, new("records.class.unresolved", "/class_id") { Target = null }]));
        Assert.Equal([("records.reference.dependency_undeclared", "/fields/0/reference", null, "records.reference.dependency_undeclared at /fields/0/reference"), UnresolvedRow], DiagnosticRows(cut));
        Assert.All(cut.FindAll("[aria-label='Diagnostics'] li"), item => Assert.False(item.HasAttribute("data-target")));
    }

    [Fact]
    public void Revision2_a_whitespace_only_target_is_non_empty_so_it_renders_verbatim_and_as_data_target()
    {
        var draft = G1Draft(); var changes = new List<RecordsDraftChange>();
        var cut = Editor(draft, changes, Preview(draft, 0, [new("records.reference.dependency_undeclared", "/fields/0/reference") { Target = " " }]));
        Assert.Equal([("records.reference.dependency_undeclared", "/fields/0/reference", " ", "records.reference.dependency_undeclared at /fields/0/reference for  ")], DiagnosticRows(cut));
    }

    [Fact]
    public void Revision2_the_warnings_list_is_preceded_by_one_visible_note_that_warnings_do_not_block_publication_and_no_note_shows_without_warnings()
    {
        var draft = G1Draft(); var changes = new List<RecordsDraftChange>();
        var cut = Editor(draft, changes, Preview(draft, 0, [Unresolved], [RetentionUnset]));
        Assert.Single(cut.FindAll("p.hl-records-authoring__note"), note => note.TextContent == "Warnings do not block publication.");
        Assert.Equal("Warnings do not block publication.", cut.Find("[aria-label='Warnings']").PreviousElementSibling!.TextContent);
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, Preview(draft, 0, [Unresolved])));
        Assert.DoesNotContain(cut.FindAll("p.hl-records-authoring__note"), note => note.TextContent == "Warnings do not block publication.");
    }

    [Fact]
    public void Revision2_warnings_render_in_their_own_Warnings_list_in_order_apart_from_the_diagnostics_and_never_as_a_refusal()
    {
        var draft = G1Draft(); var changes = new List<RecordsDraftChange>();
        var cut = Editor(draft, changes, Preview(draft, 0, [Undeclared, Unresolved], [RetentionUnset, AuditUnset]));
        Assert.Equal([RetentionText, "records.audit.unset at /audit_policy"], WarningTexts(cut));
        Assert.Equal([UndeclaredRow, UnresolvedRow], DiagnosticRows(cut));
        var diagnostics = cut.Find("[aria-label='Diagnostics']"); var warnings = cut.Find("[aria-label='Warnings']");
        Assert.False(diagnostics.Contains(warnings)); Assert.False(warnings.Contains(diagnostics));
        Assert.Null(warnings.Closest("[role='alert']"));
        Assert.Empty(changes);
    }

    [Fact]
    public void Revision2_a_warnings_only_set_shows_no_Diagnostics_list_and_absent_or_empty_warnings_show_no_Warnings_list()
    {
        var draft = G1Draft(); var changes = new List<RecordsDraftChange>();
        var cut = Editor(draft, changes, Preview(draft, 0, [], [RetentionUnset]));
        Assert.Equal([RetentionText], WarningTexts(cut)); Assert.False(HasDiagnosticsList(cut));
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, Preview(draft, 0, [Unresolved])));
        Assert.Equal([UnresolvedRow], DiagnosticRows(cut)); Assert.False(HasWarningsList(cut));
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, Preview(draft, 0, [Unresolved], [])));
        Assert.Equal([UnresolvedRow], DiagnosticRows(cut)); Assert.False(HasWarningsList(cut));
        Assert.Empty(changes);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("revision")]
    [InlineData("generation")]
    public void Revision2_shows_neither_list_when_one_member_does_not_match_the_current_draft(string member)
    {
        var draft = G1Draft(); var changes = new List<RecordsDraftChange>(); var matching = Preview(draft, 0, [Undeclared, Unresolved], [RetentionUnset]);
        var mismatched = member switch { "identity" => matching with { Identity = "eam.other" }, "revision" => matching with { ExpectedRevision = "2" }, "generation" => matching with { Generation = 1 }, _ => throw new ArgumentOutOfRangeException(nameof(member)) };
        var cut = Editor(draft, changes, mismatched);
        Assert.False(HasDiagnosticsList(cut)); Assert.False(HasWarningsList(cut)); Assert.Equal(0, NoteCount(cut));
        // Positive control: the same set, matching, shows both.
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, matching));
        Assert.Equal([UndeclaredRow, UnresolvedRow], DiagnosticRows(cut)); Assert.Equal([RetentionText], WarningTexts(cut)); Assert.Equal(1, NoteCount(cut));
        Assert.Empty(changes);
    }

    [Fact]
    public void Revision2_read_only_still_shows_both_lists()
    {
        var draft = G1Draft(); var changes = new List<RecordsDraftChange>();
        var cut = Editor(draft, changes, Preview(draft, 0, [Undeclared, Unresolved], [RetentionUnset]), readOnly: true);
        Assert.Equal([UndeclaredRow, UnresolvedRow], DiagnosticRows(cut)); Assert.Equal([RetentionText], WarningTexts(cut)); Assert.Equal(1, NoteCount(cut));
        Assert.Empty(changes);
    }

    [Fact]
    public void Revision2_an_edit_after_warnings_were_shown_moves_the_generation_so_both_lists_disappear_until_a_matching_set_arrives()
    {
        var draft = G1Draft(); var changes = new List<RecordsDraftChange>(); var atZero = Preview(draft, 0, [Undeclared, Unresolved], [RetentionUnset]);
        var cut = Editor(draft, changes, atZero);
        Assert.Equal([UndeclaredRow, UnresolvedRow], DiagnosticRows(cut)); Assert.Equal([RetentionText], WarningTexts(cut));
        // Warnings never block: the edit is accepted and reported.
        cut.Find("input[aria-label='Record type name']").Change("local");
        Assert.Single(changes); Assert.Equal(1, changes[0].Generation);
        Assert.False(HasDiagnosticsList(cut)); Assert.False(HasWarningsList(cut)); Assert.Equal(0, NoteCount(cut));
        cut.Render(parameters => parameters.Add(component => component.Value, changes[0].Draft).Add(component => component.Diagnostics, atZero));
        Assert.Equal("1", Generation(cut)); Assert.False(HasDiagnosticsList(cut)); Assert.False(HasWarningsList(cut)); Assert.Equal(0, NoteCount(cut));
        cut.Render(parameters => parameters.Add(component => component.Diagnostics, Preview(changes[0].Draft, 1, [Undeclared, Unresolved], [RetentionUnset])));
        Assert.Equal([UndeclaredRow, UnresolvedRow], DiagnosticRows(cut)); Assert.Equal([RetentionText], WarningTexts(cut));
        Assert.Single(changes);
    }

    // ---- Interface revision 3 header (semantics.edits, notEdited, hostOptions; producer spellings RecordTypeDocument.cs:60-171) ----------
    // Written from the interface, not the implementation. Controls are found by aria-label and options by visible text, never by option value.
    // Every base body carries unknown members at the body, envelope and field level, so each exact expected body also proves preservation.
    // Method names pair one-for-one with RecordTypeDraftEditor.test.tsx ("Revision3: ...").
    private static readonly IReadOnlyList<RecordsClassOption> ClassOffer = [new("eam.equipment"), new("eam.facility")];
    private static readonly IReadOnlyList<RecordsRoleOption> RoleOffer = [new("asset-manager"), new("records-clerk")];
    private static readonly string[] HeaderSelects = ["Class", "Record class", "Creation gate", "Creation role", "Amendment mode"];
    private static readonly string[] HeaderCheckboxes = ["Amendments require a reason", "Keep history"];

    private static JsonObject With(JsonObject body, params (string Member, JsonNode? Value)[] members)
    {
        var next = body.DeepClone().AsObject();
        foreach (var (member, value) in members) next[member] = value?.DeepClone();
        return next;
    }

    private static JsonObject Without(JsonObject body, params string[] members)
    {
        var next = body.DeepClone().AsObject();
        foreach (var member in members) next.Remove(member);
        return next;
    }

    // G1 (class_id eam.equipment, record_class master, no creation, amendment or history member) plus unknown members, then the case's members.
    private static JsonObject HeaderBody(params (string Member, JsonNode? Value)[] members)
    {
        var body = G1Draft().Body;
        body["future_member"] = new JsonObject { ["kept"] = new JsonArray(1, "two") };
        body["envelope"]!["future_envelope_member"] = true;
        body["fields"]![0]!["future_field_member"] = "kept";
        return With(body, members);
    }

    // Every header member at once, plus an opaque traits value and the retention clock, so each edit must change exactly its own members.
    private static JsonObject FullHeader() => HeaderBody(("traits", JsonNode.Parse("""[{"opaque":"kept"}]""")), ("retention_clock_field_id", "asset_tag"), ("creation_gate", "role"), ("creation_role_id", "records-clerk"), ("amendment_policy", JsonNode.Parse("""{"reason_required":true,"mode":"supersession","future_policy_member":"kept"}""")), ("history_policy", "enabled"));
    private static RecordsDraft DraftWith(JsonObject body) => G1Draft() with { Body = body };

    private static void AssertReported(RecordsDraftChange change, JsonObject expectedBody, int generation)
    {
        var g1 = G1Draft();
        Assert.Equal((g1.Identity, g1.VersionId, g1.ExpectedRevision, g1.DefinitionKind, generation), (change.Draft.Identity, change.Draft.VersionId, change.Draft.ExpectedRevision, change.Draft.DefinitionKind, change.Generation));
        Assert.True(JsonNode.DeepEquals(expectedBody, change.Draft.Body), change.Draft.Body.ToJsonString());
    }

    // Absent options are left unset, so each option parameter keeps its default.
    private IRenderedComponent<HarborlineRecordTypeDraftEditor> Header(RecordsDraft value, List<RecordsDraftChange> changes, IReadOnlyList<RecordsClassOption>? classOptions = null, IReadOnlyList<RecordsRoleOption>? roleOptions = null, bool readOnly = false)
        => Render<HarborlineRecordTypeDraftEditor>(parameters =>
        {
            parameters.Add(component => component.Value, value).Add(component => component.KindOptions, KindOptions).Add(component => component.ReadOnly, readOnly).Add(component => component.DraftChanged, (RecordsDraftChange change) => changes.Add(change));
            if (classOptions is not null) parameters.Add(component => component.ClassOptions, classOptions);
            if (roleOptions is not null) parameters.Add(component => component.RoleOptions, roleOptions);
        });

    private static IElement HeaderSelect(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, string name) => cut.Find($"select[aria-label='{name}']");
    private static bool HasSelect(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, string name) => cut.FindAll($"select[aria-label='{name}']").Count > 0;
    private static IReadOnlyList<IElement> Options(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, string name) => cut.FindAll($"select[aria-label='{name}'] option");
    private static string[] OptionTexts(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, string name) => [.. Options(cut, name).Select(option => option.TextContent)];
    private static string OptionValue(IElement option) => option.GetAttribute("value") ?? option.TextContent;
    // The shown option: one marked selected, else the one whose value the select carries (Blazor renders the bound value on the select).
    private static string? SelectedText(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, string name)
    {
        var options = Options(cut, name); var value = HeaderSelect(cut, name).GetAttribute("value") ?? "";
        return (options.FirstOrDefault(option => option.HasAttribute("selected")) ?? options.FirstOrDefault(option => OptionValue(option) == value))?.TextContent;
    }
    private static void Choose(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, string name, string text) => HeaderSelect(cut, name).Change(OptionValue(Assert.Single(Options(cut, name), option => option.TextContent == text)));
    private static IElement Checkbox(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, string name) => cut.Find($"input[type='checkbox'][aria-label='{name}']");
    private static bool IsChecked(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, string name) => Checkbox(cut, name).HasAttribute("checked");
    private static void Toggle(IRenderedComponent<HarborlineRecordTypeDraftEditor> cut, string name) => Checkbox(cut, name).Change(!IsChecked(cut, name));
    private static bool IsDisabled(IElement control) => control.HasAttribute("disabled") || control.Closest("fieldset[disabled]") is not null;

    [Fact]
    public void Revision3_header_controls_appear_only_after_the_definition_kind_is_chosen_and_nothing_is_selected_for_the_host()
    {
        var changes = new List<RecordsDraftChange>(); var cut = Header(RecordsDraft.Empty, changes, ClassOffer, RoleOffer);
        Assert.All(HeaderSelects, name => Assert.False(HasSelect(cut, name), name));
        Assert.All(HeaderCheckboxes, name => Assert.Empty(cut.FindAll($"input[type='checkbox'][aria-label='{name}']")));
        cut.Find("select[aria-label='Definition kind']").Change("Records");
        var change = Assert.Single(changes);
        Assert.Equal(("", "", (string?)null, (string?)"Records", 1, 0), (change.Draft.Identity, change.Draft.VersionId, change.Draft.ExpectedRevision, change.Draft.DefinitionKind, change.Generation, change.Draft.Body.Count));
        Assert.Equal(["Not set", "eam.equipment", "eam.facility"], OptionTexts(cut, "Class")); Assert.Equal("Not set", SelectedText(cut, "Class"));
        Assert.True(HasSelect(cut, "Record class"));
        Assert.Equal(["Not set (form_only)", "form_only", "role", "engine"], OptionTexts(cut, "Creation gate")); Assert.Equal("Not set (form_only)", SelectedText(cut, "Creation gate"));
        Assert.False(HasSelect(cut, "Creation role")); Assert.False(HasSelect(cut, "Amendment mode"));
        Assert.False(IsChecked(cut, "Amendments require a reason")); Assert.False(IsChecked(cut, "Keep history"));
        Assert.Single(changes);
    }

    [Fact]
    public void Revision3_Class_writes_an_offered_class_id_exactly_and_Not_set_removes_the_member()
    {
        var body = HeaderBody(); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes, ClassOffer);
        Assert.Equal(["Not set", "eam.equipment", "eam.facility"], OptionTexts(cut, "Class")); Assert.Equal("eam.equipment", SelectedText(cut, "Class")); Assert.Empty(changes);
        Choose(cut, "Class", "eam.facility");
        AssertReported(Assert.Single(changes), With(body, ("class_id", "eam.facility")), 1);
        Choose(cut, "Class", "Not set");
        Assert.Equal(2, changes.Count); AssertReported(changes[1], Without(body, "class_id"), 2);
        Assert.False(changes[1].Draft.Body.ContainsKey("class_id"));
    }

    [Fact]
    public void Revision3_a_current_class_the_host_does_not_offer_is_shown_as_not_offered_and_kept()
    {
        var body = HeaderBody(("class_id", "eam.legacy")); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes, ClassOffer);
        Assert.Equal("Not set", OptionTexts(cut, "Class")[0]);
        Assert.Equal(["Not set", "eam.equipment", "eam.facility", "eam.legacy (not offered)"], OptionTexts(cut, "Class").Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("eam.legacy (not offered)", SelectedText(cut, "Class")); Assert.Empty(changes);
        Toggle(cut, "Keep history");
        AssertReported(Assert.Single(changes), With(body, ("history_policy", "enabled")), 1);
        Assert.Equal("eam.legacy (not offered)", SelectedText(cut, "Class"));
    }

    [Fact]
    public void Revision3_with_no_class_options_only_Not_set_and_the_current_class_are_offered()
    {
        var first = new List<RecordsDraftChange>(); var cut = Header(DraftWith(HeaderBody()), first);
        Assert.Equal(["Not set", "eam.equipment (not offered)"], OptionTexts(cut, "Class").Order(StringComparer.Ordinal).ToArray()); Assert.Equal("eam.equipment (not offered)", SelectedText(cut, "Class"));
        var second = new List<RecordsDraftChange>(); var other = Header(DraftWith(Without(HeaderBody(), "class_id")), second);
        Assert.Equal(["Not set"], OptionTexts(other, "Class")); Assert.Equal("Not set", SelectedText(other, "Class"));
        Assert.Empty(first); Assert.Empty(second);
    }

    [Fact]
    public void Revision3_the_editor_never_selects_a_class_or_a_creation_role_on_its_own_even_when_one_option_is_offered()
    {
        var changes = new List<RecordsDraftChange>();
        var cut = Header(DraftWith(Without(HeaderBody(("creation_gate", "role")), "class_id")), changes, [new("eam.equipment")], [new("asset-manager")]);
        Assert.Equal("Not set", SelectedText(cut, "Class")); Assert.Equal("Not set", SelectedText(cut, "Creation role"));
        Assert.Empty(changes);
    }

    [Theory]
    [InlineData("reference", "master")]
    [InlineData("master", "transactional")]
    [InlineData("transactional", "reference")]
    public void Revision3_Record_class_writes_the_producer_spelling_exactly(string spelling, string current)
    {
        var body = HeaderBody(("record_class", current)); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes);
        Assert.Equal(current, SelectedText(cut, "Record class"));
        Choose(cut, "Record class", spelling);
        AssertReported(Assert.Single(changes), With(body, ("record_class", spelling)), 1);
    }

    [Fact]
    public void Revision3_an_unknown_current_record_class_spelling_is_shown_and_kept()
    {
        var body = HeaderBody(("record_class", "Master")); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes);
        Assert.Contains("Master", SelectedText(cut, "Record class")); Assert.Empty(changes);
        Toggle(cut, "Keep history");
        AssertReported(Assert.Single(changes), With(body, ("history_policy", "enabled")), 1);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("engine")]
    [InlineData("form_only")]
    public void Revision3_Creation_gate_writes_the_chosen_gate_exactly(string gate)
    {
        var body = HeaderBody(); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes, roleOptions: RoleOffer);
        Choose(cut, "Creation gate", gate);
        // Offered roles are never selected for the host: the gate alone is written.
        AssertReported(Assert.Single(changes), With(body, ("creation_gate", gate)), 1);
        Assert.Equal(gate, SelectedText(cut, "Creation gate"));
        if (gate == "role") Assert.Equal("Not set", SelectedText(cut, "Creation role")); else Assert.False(HasSelect(cut, "Creation role"));
    }

    [Fact]
    public void Revision3_Not_set_form_only_removes_creation_gate()
    {
        var body = HeaderBody(("creation_gate", "engine")); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes);
        Choose(cut, "Creation gate", "Not set (form_only)");
        AssertReported(Assert.Single(changes), Without(body, "creation_gate"), 1);
        Assert.False(changes[0].Draft.Body.ContainsKey("creation_gate"));
    }

    [Theory]
    [InlineData("form_only", "form_only")]
    [InlineData("engine", "engine")]
    [InlineData("Not set (form_only)", null)]
    public void Revision3_choosing_a_gate_other_than_role_removes_creation_role_id_in_the_same_change(string text, string? gate)
    {
        var body = HeaderBody(("creation_gate", "role"), ("creation_role_id", "records-clerk")); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes, roleOptions: RoleOffer);
        Choose(cut, "Creation gate", text);
        var expected = Without(body, "creation_gate", "creation_role_id"); if (gate is not null) expected["creation_gate"] = gate;
        AssertReported(Assert.Single(changes), expected, 1);
        Assert.False(changes[0].Draft.Body.ContainsKey("creation_role_id")); Assert.False(HasSelect(cut, "Creation role"));
    }

    [Fact]
    public void Revision3_choosing_role_keeps_an_existing_creation_role_id()
    {
        var body = HeaderBody(("creation_gate", "engine"), ("creation_role_id", "records-clerk")); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes, roleOptions: RoleOffer);
        Choose(cut, "Creation gate", "role");
        AssertReported(Assert.Single(changes), With(body, ("creation_gate", "role")), 1);
        Assert.Equal("records-clerk", SelectedText(cut, "Creation role"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("form_only")]
    [InlineData("engine")]
    public void Revision3_Creation_role_is_not_offered_when_the_gate_is(string? gate)
    {
        var body = HeaderBody(("creation_role_id", "records-clerk")); if (gate is not null) body["creation_gate"] = gate;
        var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes, roleOptions: RoleOffer);
        Assert.False(HasSelect(cut, "Creation role")); Assert.Empty(changes);
    }

    [Fact]
    public void Revision3_Creation_role_writes_an_offered_role_exactly_and_Not_set_removes_the_member()
    {
        var body = HeaderBody(("creation_gate", "role")); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes, roleOptions: RoleOffer);
        Assert.Equal(["Not set", "asset-manager", "records-clerk"], OptionTexts(cut, "Creation role")); Assert.Equal("Not set", SelectedText(cut, "Creation role")); Assert.Empty(changes);
        Choose(cut, "Creation role", "records-clerk");
        AssertReported(Assert.Single(changes), With(body, ("creation_role_id", "records-clerk")), 1);
        Choose(cut, "Creation role", "Not set");
        Assert.Equal(2, changes.Count); AssertReported(changes[1], body, 2);
        Assert.False(changes[1].Draft.Body.ContainsKey("creation_role_id"));
    }

    [Fact]
    public void Revision3_a_current_role_the_host_does_not_offer_is_shown_as_not_offered_and_kept_and_no_role_options_offer_only_Not_set()
    {
        var body = HeaderBody(("creation_gate", "role"), ("creation_role_id", "legacy-role")); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes, roleOptions: RoleOffer);
        Assert.Equal("Not set", OptionTexts(cut, "Creation role")[0]);
        Assert.Equal(["Not set", "asset-manager", "legacy-role (not offered)", "records-clerk"], OptionTexts(cut, "Creation role").Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("legacy-role (not offered)", SelectedText(cut, "Creation role")); Assert.Empty(changes);
        Toggle(cut, "Keep history");
        AssertReported(Assert.Single(changes), With(body, ("history_policy", "enabled")), 1);
        var second = new List<RecordsDraftChange>(); var other = Header(DraftWith(HeaderBody(("creation_gate", "role"))), second);
        Assert.Equal(["Not set"], OptionTexts(other, "Creation role")); Assert.Empty(second);
    }

    [Fact]
    public void Revision3_checking_writes_reason_required_true_Amendment_mode_adds_and_removes_mode_and_unchecking_removes_the_policy()
    {
        var body = HeaderBody(); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes);
        Assert.False(IsChecked(cut, "Amendments require a reason")); Assert.False(HasSelect(cut, "Amendment mode"));
        Toggle(cut, "Amendments require a reason");
        AssertReported(changes[^1], With(body, ("amendment_policy", JsonNode.Parse("""{"reason_required":true}"""))), 1);
        Assert.True(IsChecked(cut, "Amendments require a reason"));
        Assert.Equal(["No mode", "supersession"], OptionTexts(cut, "Amendment mode")); Assert.Equal("No mode", SelectedText(cut, "Amendment mode"));
        Choose(cut, "Amendment mode", "supersession");
        AssertReported(changes[^1], With(body, ("amendment_policy", JsonNode.Parse("""{"reason_required":true,"mode":"supersession"}"""))), 2);
        Choose(cut, "Amendment mode", "No mode");
        AssertReported(changes[^1], With(body, ("amendment_policy", JsonNode.Parse("""{"reason_required":true}"""))), 3);
        Toggle(cut, "Amendments require a reason");
        AssertReported(changes[^1], body, 4);
        Assert.False(changes[3].Draft.Body.ContainsKey("amendment_policy")); Assert.False(HasSelect(cut, "Amendment mode")); Assert.Equal(4, changes.Count);
    }

    // Not in the interface's required list: a malformed host policy must still render, show as absent, and never be spread into the new one.
    [Theory]
    [InlineData("null")]
    [InlineData("\"ab\"")]
    [InlineData("[1]")]
    public void Revision3_a_malformed_amendment_policy_renders_unchecked_with_no_Amendment_mode_and_checking_replaces_it_with_reason_required_true(string policy)
    {
        var body = HeaderBody(("amendment_policy", JsonNode.Parse(policy))); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes);
        Assert.False(IsChecked(cut, "Amendments require a reason")); Assert.False(HasSelect(cut, "Amendment mode")); Assert.Empty(changes);
        Toggle(cut, "Amendments require a reason");
        AssertReported(changes[^1], With(body, ("amendment_policy", JsonNode.Parse("""{"reason_required":true}"""))), 1);
    }

    [Fact]
    public void Revision3_a_non_string_header_value_shows_as_not_set_and_is_kept_unchanged()
    {
        var body = HeaderBody(("class_id", JsonValue.Create(42)), ("record_class", JsonValue.Create(7)), ("creation_gate", JsonValue.Create(true))); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes, classOptions: [new("eam.equipment")]);
        Assert.Equal("Not set", SelectedText(cut, "Class")); Assert.Equal("Not set", SelectedText(cut, "Record class")); Assert.Equal("Not set (form_only)", SelectedText(cut, "Creation gate"));
        Assert.Empty(changes);
    }

    [Fact]
    // A host-declared policy cannot be unchecked (the producer has no 'off' value and save carries an omitted declaration forward).
    public void Revision3_No_mode_keeps_reason_required_and_the_other_policy_members_and_a_host_declared_policy_cannot_be_unchecked()
    {
        var body = HeaderBody(("amendment_policy", JsonNode.Parse("""{"reason_required":true,"mode":"supersession","future_policy_member":"kept"}""")));
        var first = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), first);
        Assert.True(IsChecked(cut, "Amendments require a reason")); Assert.Equal("supersession", SelectedText(cut, "Amendment mode"));
        Choose(cut, "Amendment mode", "No mode");
        AssertReported(Assert.Single(first), With(body, ("amendment_policy", JsonNode.Parse("""{"reason_required":true,"future_policy_member":"kept"}"""))), 1);
        var second = new List<RecordsDraftChange>(); var other = Header(DraftWith(body), second);
        Assert.True(IsDisabled(Checkbox(other, "Amendments require a reason")));
        Checkbox(other, "Amendments require a reason").Change(false);
        Assert.Empty(second);
    }

    [Fact]
    public void Revision3_checking_with_an_existing_policy_writes_reason_required_true_and_keeps_its_other_members()
    {
        // A policy present without reason_required true (malformed for the producer, carried as given): unchecked, with its mode shown.
        var body = HeaderBody(("amendment_policy", JsonNode.Parse("""{"mode":"supersession","future_policy_member":"kept"}"""))); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes);
        Assert.False(IsChecked(cut, "Amendments require a reason")); Assert.Equal("supersession", SelectedText(cut, "Amendment mode")); Assert.Empty(changes);
        Toggle(cut, "Amendments require a reason");
        AssertReported(Assert.Single(changes), With(body, ("amendment_policy", JsonNode.Parse("""{"mode":"supersession","future_policy_member":"kept","reason_required":true}"""))), 1);
    }

    [Fact]
    public void Revision3_Keep_history_writes_history_policy_enabled_and_unchecking_removes_it()
    {
        var body = HeaderBody(); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes);
        Assert.False(IsChecked(cut, "Keep history"));
        Toggle(cut, "Keep history");
        AssertReported(Assert.Single(changes), With(body, ("history_policy", "enabled")), 1); Assert.True(IsChecked(cut, "Keep history"));
        Toggle(cut, "Keep history");
        Assert.Equal(2, changes.Count); AssertReported(changes[1], body, 2);
        Assert.False(changes[1].Draft.Body.ContainsKey("history_policy"));
    }

    [Theory]
    [InlineData("Class")]
    [InlineData("Class Not set")]
    [InlineData("Record class")]
    [InlineData("Creation gate engine")]
    [InlineData("Creation role")]
    [InlineData("Creation role Not set")]
    [InlineData("Amendment mode No mode")]
    public void Revision3_the_edit_changes_only_its_own_members_and_reports_exactly_one_change_at_the_next_generation(string edit)
    {
        var body = FullHeader(); var given = body.DeepClone(); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes, ClassOffer, RoleOffer);
        JsonObject expected;
        switch (edit)
        {
            case "Class": Choose(cut, "Class", "eam.facility"); expected = With(FullHeader(), ("class_id", "eam.facility")); break;
            case "Class Not set": Choose(cut, "Class", "Not set"); expected = Without(FullHeader(), "class_id"); break;
            case "Record class": Choose(cut, "Record class", "reference"); expected = With(FullHeader(), ("record_class", "reference")); break;
            case "Creation gate engine": Choose(cut, "Creation gate", "engine"); expected = With(Without(FullHeader(), "creation_role_id"), ("creation_gate", "engine")); break;
            case "Creation role": Choose(cut, "Creation role", "asset-manager"); expected = With(FullHeader(), ("creation_role_id", "asset-manager")); break;
            case "Creation role Not set": Choose(cut, "Creation role", "Not set"); expected = Without(FullHeader(), "creation_role_id"); break;
            case "Amendment mode No mode": Choose(cut, "Amendment mode", "No mode"); expected = With(FullHeader(), ("amendment_policy", JsonNode.Parse("""{"reason_required":true,"future_policy_member":"kept"}"""))); break;
            default: throw new ArgumentOutOfRangeException(nameof(edit));
        }
        AssertReported(Assert.Single(changes), expected, 1);
        Assert.True(JsonNode.DeepEquals(given, body), body.ToJsonString());
    }

    // From the independent review: the producer has no 'off' policy value and save carries an omitted declaration forward (ADR-0097).
    [Fact]
    public void Revision3_a_policy_the_host_draft_declares_cannot_be_removed_so_both_checkboxes_are_checked_and_disabled_and_toggling_reports_no_change()
    {
        var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(FullHeader()), changes, ClassOffer, RoleOffer);
        Assert.All(HeaderCheckboxes, name => { Assert.True(IsChecked(cut, name), name); Assert.True(IsDisabled(Checkbox(cut, name)), name); });
        Checkbox(cut, "Amendments require a reason").Change(false); Checkbox(cut, "Keep history").Change(false);
        Assert.Empty(changes);
    }

    [Fact]
    public void Revision3_a_clean_replacement_that_declares_history_disables_its_checkbox_and_one_that_does_not_leaves_it_enabled()
    {
        var body = HeaderBody(); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes);
        Assert.False(IsDisabled(Checkbox(cut, "Keep history")));
        cut.Render(parameters => parameters.Add(component => component.Value, DraftWith(With(body, ("history_policy", "enabled"))) with { VersionId = "2.0.0" }));
        Assert.True(IsChecked(cut, "Keep history")); Assert.True(IsDisabled(Checkbox(cut, "Keep history")));
        cut.Render(parameters => parameters.Add(component => component.Value, DraftWith(HeaderBody()) with { VersionId = "3.0.0" }));
        Assert.False(IsChecked(cut, "Keep history")); Assert.False(IsDisabled(Checkbox(cut, "Keep history")));
    }

    [Fact]
    public void Revision3_two_header_edits_on_one_render_build_on_the_local_draft_so_the_second_change_keeps_the_first()
    {
        var body = HeaderBody(); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes, ClassOffer);
        Choose(cut, "Class", "eam.facility"); Toggle(cut, "Keep history");
        Assert.Equal(2, changes.Count);
        AssertReported(changes[0], With(body, ("class_id", "eam.facility")), 1);
        AssertReported(changes[1], With(body, ("class_id", "eam.facility"), ("history_policy", "enabled")), 2);
    }

    [Fact]
    public void Revision3_malformed_checkbox_values_show_unchecked_and_enabled_and_checking_writes_the_admitted_value()
    {
        var body = HeaderBody(("history_policy", "disabled"), ("amendment_policy", JsonNode.Parse("""{"reason_required":false}"""))); var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(body), changes);
        Assert.All(HeaderCheckboxes, name => { Assert.False(IsChecked(cut, name), name); Assert.False(IsDisabled(Checkbox(cut, name)), name); });
        Toggle(cut, "Keep history"); Toggle(cut, "Amendments require a reason");
        Assert.Equal(2, changes.Count);
        AssertReported(changes[0], With(body, ("history_policy", "enabled")), 1);
        AssertReported(changes[1], With(body, ("history_policy", "enabled"), ("amendment_policy", JsonNode.Parse("""{"reason_required":true}"""))), 2);
    }

    [Fact]
    public void Revision3_a_non_string_creation_role_an_unknown_gate_spelling_an_unknown_mode_and_an_empty_class_are_shown_as_given_and_kept()
    {
        var changes = new List<RecordsDraftChange>();
        var first = Header(DraftWith(HeaderBody(("creation_gate", "role"), ("creation_role_id", JsonValue.Create(42)))), changes, roleOptions: RoleOffer);
        Assert.Equal("Not set", SelectedText(first, "Creation role"));
        var second = Header(DraftWith(HeaderBody(("creation_gate", "form-only"), ("amendment_policy", JsonNode.Parse("""{"reason_required":true,"mode":"edit_in_place"}""")), ("class_id", ""))), changes, ClassOffer);
        Assert.Equal("form-only (not a current spelling)", SelectedText(second, "Creation gate")); Assert.Equal("edit_in_place (not a current spelling)", SelectedText(second, "Amendment mode"));
        Assert.Equal(["Not set", "eam.equipment", "eam.facility"], OptionTexts(second, "Class")); Assert.Equal("Not set", SelectedText(second, "Class"));
        Assert.Empty(changes);
    }

    [Fact]
    public void Revision3_read_only_disables_every_header_control_and_reports_no_change()
    {
        var changes = new List<RecordsDraftChange>(); var cut = Header(DraftWith(FullHeader()), changes, ClassOffer, RoleOffer, readOnly: true);
        Assert.All(HeaderSelects, name => Assert.True(IsDisabled(HeaderSelect(cut, name)), name));
        Assert.All(HeaderCheckboxes, name => Assert.True(IsDisabled(Checkbox(cut, name)), name));
        Choose(cut, "Class", "eam.facility"); Choose(cut, "Class", "Not set"); Choose(cut, "Record class", "reference"); Choose(cut, "Creation gate", "engine"); Choose(cut, "Creation role", "asset-manager"); Choose(cut, "Amendment mode", "No mode");
        Toggle(cut, "Amendments require a reason"); Toggle(cut, "Keep history");
        Assert.Empty(changes);
        Assert.Equal("eam.equipment", SelectedText(cut, "Class")); Assert.Equal("role", SelectedText(cut, "Creation gate")); Assert.Equal("records-clerk", SelectedText(cut, "Creation role"));
        Assert.True(IsChecked(cut, "Amendments require a reason")); Assert.True(IsChecked(cut, "Keep history"));
    }
}
