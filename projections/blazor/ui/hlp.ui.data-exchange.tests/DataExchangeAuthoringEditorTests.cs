using Bunit;
using Harborline.UIAdapters.Blazor.Components.DataExchange;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class DataExchangeAuthoringEditorTests : BunitContext
{
    private static readonly DataExchangeAuthoringCatalogue Catalogue = new(
        [new("connector.csv/v1", "CSV upload")],
        [new("records.customer/v1", "Customer")],
        [new("string", "Text")],
        [new("trimToNull", "Trim to null")],
        [new("schedule.nightly", "Nightly")]);

    [Fact]
    public void Authors_the_bounded_inbound_definition_and_emits_host_intents()
    {
        DataExchangeAuthoringDraft? changed = null;
        var discovered = 0;
        var dryRuns = 0;
        var cut = Render<HarborlineDataExchangeAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, DataExchangeAuthoringDraft.Empty)
            .Add(component => component.Catalogue, Catalogue)
            .Add(component => component.CanCommit, false)
            .Add(component => component.CanPublish, true)
            .Add(component => component.AuthoringRefusals, [new("definition", "mapping.target_forbidden", "/definitions/records.customer")])
            .Add(component => component.ValueChanged, value => changed = value)
            .Add(component => component.DiscoverSource, () => discovered++)
            .Add(component => component.CreateDryRun, () => dryRuns++));

        Assert.Contains("hl:tabular-mapping/v1", cut.Markup);
        Assert.Contains("https://schemas.harborline.software/mapping/tabular/v1", cut.Markup);
        Assert.Contains("1.0.0", cut.Markup);
        Assert.NotNull(cut.Find("input[aria-label='Secret reference']"));
        Assert.Empty(cut.FindAll("input[aria-label='Password']"));
        Assert.NotNull(cut.Find("select[aria-label='Format']"));
        Assert.Equal(["append", "overwrite", "append_dedup"],
            cut.Find("select[aria-label='Replay policy']").Children.Select(option => option.GetAttribute("value")));
        Assert.NotNull(cut.Find("input[aria-label='Reference dataset']"));
        Assert.True(cut.FindAll("button").Single(button => button.TextContent == "Publish definition").HasAttribute("disabled"));
        Assert.Equal("/definitions/records.customer", cut.Find("a").GetAttribute("href"));

        cut.Find("input[aria-label='Definition name']").Change("Customer import");
        Assert.Equal("Customer import", changed?.Name);
        cut.FindAll("button").Single(button => button.TextContent == "Discover source").Click();
        cut.FindAll("button").Single(button => button.TextContent == "Create dry run").Click();
        Assert.Equal(1, discovered);
        Assert.Equal(1, dryRuns);
        Assert.True(cut.Find("button[aria-label='Commit reviewed run']").HasAttribute("disabled"));
    }

    [Fact]
    public void Stale_review_cannot_be_promoted_and_discovered_shape_can_be_narrowed()
    {
        DataExchangeAuthoringDraft? changed = null;
        var commits = 0;
        var value = DataExchangeAuthoringDraft.Empty with
        {
            DiscoveredColumns = [new("CustomerNumber", true), new("Ignored", true)],
        };
        var run = new DataExchangeRunSummary(
            "dry-7", "Ready", true, "cursor:7", new(0, 0, 0, 0, 0, 0), ["mapping.changed"]);
        var cut = Render<HarborlineDataExchangeAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, value)
            .Add(component => component.Catalogue, Catalogue)
            .Add(component => component.Run, run)
            .Add(component => component.CanCommit, true)
            .Add(component => component.ValueChanged, next => changed = next)
            .Add(component => component.CommitReviewedRun, () => commits++));

        cut.Find("input[aria-label='Include Ignored']").Change(false);
        Assert.False(changed!.DiscoveredColumns[1].Selected);
        Assert.Contains("mapping.changed", cut.Markup);
        cut.Find("button[aria-label='Commit reviewed run']").Click();
        Assert.Equal(0, commits);
    }

    [Fact]
    public void An_unselected_format_remains_unselected_until_the_author_chooses()
    {
        var cut = Render<HarborlineDataExchangeAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, DataExchangeAuthoringDraft.Empty with { FormatCapability = "" })
            .Add(component => component.Catalogue, Catalogue));

        var select = Assert.IsAssignableFrom<AngleSharp.Html.Dom.IHtmlSelectElement>(cut.Find("select[aria-label='Format']"));
        Assert.Equal("", select.Value);
        Assert.Equal("Choose a format", select.SelectedOptions.Single().TextContent);
        Assert.Equal(["", "csv"], select.Options.Select(option => option.Value));
    }

    [Theory]
    [InlineData("select[aria-label='Source capability']", true, "LABEL")]
    [InlineData("select[aria-label='Mapping 1 source column']", false, "SELECT")]
    [InlineData("input[aria-label='Mapping 1 null']", false, "INPUT")]
    [InlineData("input[aria-label='External key columns']", true, "LABEL")]
    [InlineData("input[aria-label='Reference dataset']", false, "INPUT")]
    public void Inline_controls_do_not_gain_layout_changing_text_gaps(string selector, bool useParent, string nextTag)
    {
        var value = DataExchangeAuthoringDraft.Empty with
        {
            DiscoveredColumns = [new("CustomerNumber", true)],
            Mappings = [new("CustomerNumber", "records.customer/v1", "/customerNumber", "string", true, "", "", "", "trimToNull")],
        };
        var cut = Render<HarborlineDataExchangeAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, value)
            .Add(component => component.Catalogue, Catalogue));

        var control = cut.Find(selector);
        var inlineNode = useParent ? control.ParentElement! : control;
        var next = Assert.IsAssignableFrom<AngleSharp.Dom.IElement>(inlineNode.NextSibling);
        Assert.Equal(nextTag, next.TagName);
    }

    // ADR 0096 lane conformance. The cases come from conformance/hlp.ui.data-exchange/fixtures.yaml,
    // which the React lane test reads too, so both lanes assert the same values.
    [Fact, Trait("Holds", "data-exchange-auth-9")]
    public void ReadOnly_admission_disables_every_input_and_publish()
    {
        var fixture = FindFixture("data-exchange.read-only");
        var input = fixture.GetProperty("input");
        var expected = fixture.GetProperty("expected");
        var intents = 0;
        var cut = Render<HarborlineDataExchangeAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, DraftOf(input.GetProperty("value")))
            .Add(component => component.Catalogue, Catalogue)
            .Add(component => component.Run, RunOf(input.GetProperty("run")))
            .Add(component => component.CanCommit, input.GetProperty("canCommit").GetBoolean())
            .Add(component => component.CanPublish, input.GetProperty("canPublish").GetBoolean())
            .Add(component => component.ReadOnly, input.GetProperty("readOnly").GetBoolean())
            .Add(component => component.ValueChanged, _ => intents++)
            .Add(component => component.DiscoverSource, () => intents++)
            .Add(component => component.CreateDryRun, () => intents++)
            .Add(component => component.CommitReviewedRun, () => intents++)
            .Add(component => component.SaveDraft, () => intents++)
            .Add(component => component.PublishDefinition, () => intents++));

        var controls = cut.FindAll("input, select, textarea, button");
        Assert.True(controls.Count > 20);
        Assert.True(expected.GetProperty("everyControlDisabled").GetBoolean());
        Assert.All(controls, control => Assert.True(control.HasAttribute("disabled"), control.OuterHtml));
        Assert.Equal(!expected.GetProperty("publishEnabled").GetBoolean(), Button(cut, "Publish definition").HasAttribute("disabled"));
        Assert.Equal(!expected.GetProperty("commitEnabled").GetBoolean(), cut.Find("button[aria-label='Commit reviewed run']").HasAttribute("disabled"));
        Assert.Equal(expected.GetProperty("staleness").GetString(), Evidence(cut, "Staleness"));
        Assert.Equal(0, intents);
    }

    [Fact]
    public void An_editable_admission_keeps_the_authoring_controls_enabled()
    {
        var input = FindFixture("data-exchange.read-only").GetProperty("input");
        var cut = Render<HarborlineDataExchangeAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, DraftOf(input.GetProperty("value")))
            .Add(component => component.Catalogue, Catalogue)
            .Add(component => component.Run, RunOf(input.GetProperty("run")))
            .Add(component => component.CanCommit, true)
            .Add(component => component.CanPublish, true));

        Assert.False(cut.Find("input[aria-label='Definition name']").HasAttribute("disabled"));
        Assert.False(Button(cut, "Publish definition").HasAttribute("disabled"));
        Assert.False(cut.Find("button[aria-label='Commit reviewed run']").HasAttribute("disabled"));
    }

    [Fact, Trait("Holds", "data-exchange-eng-22")]
    public void Pending_edits_survive_a_revision_change_until_the_author_keeps_or_discards_them()
    {
        var fixture = FindFixture("data-exchange.pending-revision");
        var input = fixture.GetProperty("input");
        var expected = fixture.GetProperty("expected");
        DataExchangeAuthoringDraft? changed = null;
        var intents = 0;
        var cut = Render<HarborlineDataExchangeAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, DraftOf(input.GetProperty("value")))
            .Add(component => component.Catalogue, Catalogue)
            .Add(component => component.CanPublish, true)
            .Add(component => component.ValueChanged, next => changed = next)
            .Add(component => component.CreateDryRun, () => intents++)
            .Add(component => component.PublishDefinition, () => intents++));

        cut.Find("input[aria-label='Definition name']").Change(input.GetProperty("edit").GetProperty("name").GetString());
        Assert.Equal(input.GetProperty("edit").GetProperty("name").GetString(), changed?.Name);
        Assert.Equal(input.GetProperty("value").GetProperty("expectedRevision").GetString(), changed?.ExpectedRevision);
        Assert.Empty(cut.FindAll("[role='alert']"));

        cut.Render(parameters => parameters.Add(component => component.Value, DraftOf(input.GetProperty("incoming"))));
        Assert.Contains(expected.GetProperty("pendingAlert").GetString()!, cut.Find("[role='alert']").TextContent);
        Assert.Equal(expected.GetProperty("keptName").GetString(), cut.Find("input[aria-label='Definition name']").GetAttribute("value"));
        Assert.True(Button(cut, "Publish definition").HasAttribute("disabled"));
        Assert.True(Button(cut, "Save draft").HasAttribute("disabled"));
        Assert.True(Button(cut, "Create dry run").HasAttribute("disabled"));
        Button(cut, "Publish definition").Click();
        Button(cut, "Create dry run").Click();
        Assert.Equal(0, intents);

        Button(cut, "Keep edits").Click();
        Assert.Empty(cut.FindAll("[role='alert']"));
        Assert.Equal(expected.GetProperty("keptName").GetString(), cut.Find("input[aria-label='Definition name']").GetAttribute("value"));
        Assert.False(Button(cut, "Publish definition").HasAttribute("disabled"));

        cut.Render(parameters => parameters.Add(component => component.Value, DraftOf(input.GetProperty("otherIdentity"))));
        Assert.Contains(expected.GetProperty("pendingAlert").GetString()!, cut.Find("[role='alert']").TextContent);
        Button(cut, "Discard edits").Click();
        Assert.Empty(cut.FindAll("[role='alert']"));
        Assert.Equal(expected.GetProperty("otherIdentityName").GetString(), cut.Find("input[aria-label='Definition name']").GetAttribute("value"));
        Assert.Equal(input.GetProperty("otherIdentity").GetProperty("identity").GetString(), changed?.Identity);
        Assert.Equal(expected.GetProperty("otherIdentityName").GetString(), changed?.Name);
    }

    [Fact]
    public void A_revision_change_without_pending_edits_is_adopted_silently()
    {
        var fixture = FindFixture("data-exchange.pending-revision");
        var input = fixture.GetProperty("input");
        var cut = Render<HarborlineDataExchangeAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, DraftOf(input.GetProperty("value")))
            .Add(component => component.Catalogue, Catalogue));

        cut.Render(parameters => parameters.Add(component => component.Value, DraftOf(input.GetProperty("incoming"))));
        Assert.Empty(cut.FindAll("[role='alert']"));
        Assert.Equal(fixture.GetProperty("expected").GetProperty("discardedName").GetString(), cut.Find("input[aria-label='Definition name']").GetAttribute("value"));
    }

    [Fact]
    public void Discarding_a_pending_edit_adopts_the_incoming_server_revision()
    {
        var fixture = FindFixture("data-exchange.pending-revision");
        var input = fixture.GetProperty("input");
        DataExchangeAuthoringDraft? changed = null;
        var cut = Render<HarborlineDataExchangeAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, DraftOf(input.GetProperty("value")))
            .Add(component => component.Catalogue, Catalogue)
            .Add(component => component.ValueChanged, next => changed = next));

        cut.Find("input[aria-label='Definition name']").Change(input.GetProperty("edit").GetProperty("name").GetString());
        cut.Render(parameters => parameters.Add(component => component.Value, DraftOf(input.GetProperty("incoming"))));
        Button(cut, "Discard edits").Click();
        var discarded = fixture.GetProperty("expected").GetProperty("discardedName").GetString();
        Assert.Equal(discarded, cut.Find("input[aria-label='Definition name']").GetAttribute("value"));
        Assert.Equal(input.GetProperty("incoming").GetProperty("expectedRevision").GetString(), changed?.ExpectedRevision);
        Assert.Equal(discarded, changed?.Name);
    }

    [Theory, Trait("Holds", "data-exchange-run-4")]
    [InlineData("data-exchange.run-evidence")]
    [InlineData("data-exchange.stale-review")]
    [InlineData("data-exchange.read-only")]
    public void Renders_the_server_census_and_staleness_verbatim_and_exposes_no_run_evidence_control(string id)
    {
        var fixture = FindFixture(id);
        var input = fixture.GetProperty("input");
        var expected = fixture.GetProperty("expected");
        var cut = Render<HarborlineDataExchangeAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, input.TryGetProperty("value", out var value) ? DraftOf(value) : DataExchangeAuthoringDraft.Empty)
            .Add(component => component.Catalogue, Catalogue)
            .Add(component => component.Run, RunOf(input.GetProperty("run")))
            .Add(component => component.CanCommit, input.GetProperty("canCommit").GetBoolean())
            .Add(component => component.ReadOnly, input.TryGetProperty("readOnly", out var readOnly) && readOnly.GetBoolean()));

        var section = EvidenceSection(cut);
        Assert.Equal(expected.GetProperty("staleness").GetString(), Evidence(cut, "Staleness"));
        if (expected.TryGetProperty("census", out var census)) Assert.Equal(census.GetString(), section.QuerySelector("p")!.TextContent);
        if (expected.TryGetProperty("evidence", out var evidence))
            foreach (var item in evidence.EnumerateArray())
                Assert.Contains(section.QuerySelectorAll("dd"), dd => dd.TextContent == item.GetString());
        Assert.Empty(section.QuerySelectorAll("input, select, textarea, button, [contenteditable]"));
        Assert.Equal(!expected.GetProperty("commitEnabled").GetBoolean(), cut.Find("button[aria-label='Commit reviewed run']").HasAttribute("disabled"));
    }

    [Fact]
    public void Accepts_both_catalogue_option_shapes_and_falls_back_when_formats_are_omitted()
    {
        var bare = Catalogue with { SourceCapabilities = ["connector.sftp/v1"], Schedules = ["schedule.hourly", new("schedule.nightly", "Nightly")] };
        var cut = Render<HarborlineDataExchangeAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, DataExchangeAuthoringDraft.Empty)
            .Add(component => component.Catalogue, bare));

        Assert.Contains(cut.FindAll("select[aria-label='Source capability'] option"), option => option.GetAttribute("value") == "connector.sftp/v1" && option.TextContent == "connector.sftp/v1");
        Assert.Contains(cut.FindAll("select[aria-label='Schedule reference'] option"), option => option.GetAttribute("value") == "schedule.hourly" && option.TextContent == "schedule.hourly");
        Assert.Contains(cut.FindAll("select[aria-label='Schedule reference'] option"), option => option.GetAttribute("value") == "schedule.nightly" && option.TextContent == "Nightly");
        Assert.Contains(cut.FindAll("select[aria-label='Format'] option"), option => option.GetAttribute("value") == "csv" && option.TextContent == "CSV");
    }

    [Fact]
    public void Renders_fallback_sections_for_an_empty_source_shape_no_mappings_and_no_dry_run()
    {
        var cut = Render<HarborlineDataExchangeAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, DataExchangeAuthoringDraft.Empty)
            .Add(component => component.Catalogue, Catalogue));

        Assert.Contains("No source columns discovered.", Section(cut, "Discovered source shape").TextContent);
        Assert.Contains("No mappings authored.", Section(cut, "Canonical mappings").TextContent);
        Assert.Contains("No dry run recorded.", EvidenceSection(cut).TextContent);
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.data-exchange")]
    public void Shared_fixture_conforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        var input = fixture.RootElement.GetProperty("input");
        DataExchangeRunSummary? run = null;
        if (input.TryGetProperty("run", out var runElement) && runElement.ValueKind != System.Text.Json.JsonValueKind.Null)
        {
            run = new(
                runElement.GetProperty("dryRunId").GetString()!,
                runElement.GetProperty("status").GetString()!,
                runElement.GetProperty("stale").GetBoolean(),
                runElement.GetProperty("candidateCheckpoint").GetString()!,
                runElement.TryGetProperty("census", out _) ? RunOf(runElement)!.Census : new(0, 0, 0, 0, 0, 0),
                runElement.GetProperty("refusals").EnumerateArray().Select(value => value.GetString()!).ToArray());
        }
        var cut = Render<HarborlineDataExchangeAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, input.TryGetProperty("value", out var value) ? DraftOf(value) : DataExchangeAuthoringDraft.Empty)
            .Add(component => component.ReadOnly, input.TryGetProperty("readOnly", out var readOnly) && readOnly.GetBoolean())
            .Add(component => component.Catalogue, Catalogue)
            .Add(component => component.Run, run)
            .Add(component => component.CanCommit, input.GetProperty("canCommit").GetBoolean()));
        var expected = fixture.RootElement.GetProperty("expected");
        if (expected.TryGetProperty("profile", out var profile)) Assert.Contains(profile.GetString()!, cut.Markup);
        if (expected.TryGetProperty("schemaUri", out var schema)) Assert.Contains(schema.GetString()!, cut.Markup);
        if (expected.TryGetProperty("documentVersion", out var version)) Assert.Contains(version.GetString()!, cut.Markup);
        Assert.Equal(!expected.GetProperty("commitEnabled").GetBoolean(), cut.Find("button[aria-label='Commit reviewed run']").HasAttribute("disabled"));
        if (expected.TryGetProperty("refusal", out var refusal)) Assert.Contains(refusal.GetString()!, cut.Markup);
        if (expected.TryGetProperty("staleness", out var staleness)) Assert.Equal(staleness.GetString(), Evidence(cut, "Staleness"));
        if (expected.TryGetProperty("census", out var census)) Assert.Equal(census.GetString(), EvidenceSection(cut).QuerySelector("p")!.TextContent);
    }

    private static System.Text.Json.JsonElement FindFixture(string id)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "catalog", "modules.yaml")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, "conformance", "hlp.ui.data-exchange", "fixtures.yaml");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        foreach (var element in document.RootElement.GetProperty("cases").EnumerateArray())
            if (element.GetProperty("id").GetString() == id) return element.Clone();
        throw new Xunit.Sdk.XunitException($"Fixture case {id} is missing.");
    }

    private static string Text(System.Text.Json.JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString()! : "";

    private static DataExchangeAuthoringDraft DraftOf(System.Text.Json.JsonElement value) => DataExchangeAuthoringDraft.Empty with
    {
        Identity = Text(value, "identity"),
        ExpectedRevision = Text(value, "expectedRevision"),
        Name = Text(value, "name"),
        DiscoveredColumns = value.TryGetProperty("discoveredColumns", out var columns)
            ? columns.EnumerateArray().Select(column => new DiscoveredSourceColumn(Text(column, "name"), column.GetProperty("selected").GetBoolean())).ToArray()
            : [],
        Mappings = value.TryGetProperty("mappings", out var mappings)
            ? mappings.EnumerateArray().Select(mapping => new DataExchangeMappingRow(
                Text(mapping, "sourceColumn"), Text(mapping, "canonicalTarget"), Text(mapping, "targetPointer"), Text(mapping, "datatype"),
                mapping.GetProperty("required").GetBoolean(), Text(mapping, "nullValue"), Text(mapping, "defaultValue"), Text(mapping, "separator"), Text(mapping, "transform"))).ToArray()
            : [],
    };

    private static DataExchangeRunSummary? RunOf(System.Text.Json.JsonElement run)
    {
        if (run.ValueKind == System.Text.Json.JsonValueKind.Null) return null;
        var census = run.GetProperty("census");
        return new(
            Text(run, "dryRunId"), Text(run, "status"), run.GetProperty("stale").GetBoolean(), Text(run, "candidateCheckpoint"),
            new(census.GetProperty("applied").GetInt32(), census.GetProperty("skipped").GetInt32(), census.GetProperty("conflicted").GetInt32(),
                census.GetProperty("rejected").GetInt32(), census.GetProperty("failed").GetInt32(), census.GetProperty("halted").GetInt32()),
            run.GetProperty("refusals").EnumerateArray().Select(value => value.GetString()!).ToArray(),
            run.TryGetProperty("batchIdentity", out var batch) ? batch.GetString() : null);
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<HarborlineDataExchangeAuthoringEditor> cut, string text) =>
        cut.FindAll("button").Single(button => button.TextContent == text);

    private static AngleSharp.Dom.IElement Section(IRenderedComponent<HarborlineDataExchangeAuthoringEditor> cut, string legend) =>
        cut.FindAll("fieldset").Single(fieldset => fieldset.QuerySelector("legend")?.TextContent == legend);

    private static AngleSharp.Dom.IElement EvidenceSection(IRenderedComponent<HarborlineDataExchangeAuthoringEditor> cut) =>
        Section(cut, "Persisted run evidence");

    private static string? Evidence(IRenderedComponent<HarborlineDataExchangeAuthoringEditor> cut, string term) =>
        EvidenceSection(cut).QuerySelectorAll("dt").SingleOrDefault(dt => dt.TextContent == term)?.NextElementSibling?.TextContent;
}
