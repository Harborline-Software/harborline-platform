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
            .Add(component => component.ValueChanged, value => changed = value)
            .Add(component => component.DiscoverSource, () => discovered++)
            .Add(component => component.CreateDryRun, () => dryRuns++));

        Assert.Contains("hl:tabular-mapping/v1", cut.Markup);
        Assert.Contains("https://schemas.harborline.software/mapping/tabular/v1", cut.Markup);
        Assert.Contains("1.0.0", cut.Markup);
        Assert.NotNull(cut.Find("input[aria-label='Secret reference']"));
        Assert.Empty(cut.FindAll("input[aria-label='Password']"));

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
                new(0, 0, 0, 0, 0, 0),
                runElement.GetProperty("refusals").EnumerateArray().Select(value => value.GetString()!).ToArray());
        }
        var cut = Render<HarborlineDataExchangeAuthoringEditor>(parameters => parameters
            .Add(component => component.Value, DataExchangeAuthoringDraft.Empty)
            .Add(component => component.Catalogue, Catalogue)
            .Add(component => component.Run, run)
            .Add(component => component.CanCommit, input.GetProperty("canCommit").GetBoolean()));
        var expected = fixture.RootElement.GetProperty("expected");
        if (expected.TryGetProperty("profile", out var profile)) Assert.Contains(profile.GetString()!, cut.Markup);
        if (expected.TryGetProperty("schemaUri", out var schema)) Assert.Contains(schema.GetString()!, cut.Markup);
        if (expected.TryGetProperty("documentVersion", out var version)) Assert.Contains(version.GetString()!, cut.Markup);
        Assert.Equal(!expected.GetProperty("commitEnabled").GetBoolean(), cut.Find("button[aria-label='Commit reviewed run']").HasAttribute("disabled"));
        if (expected.TryGetProperty("refusal", out var refusal)) Assert.Contains(refusal.GetString()!, cut.Markup);
    }
}
