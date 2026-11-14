using Bunit;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Linq;
using Harborline.UIAdapters.Blazor.Components.ReportAuthoring;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ReportAuthoringTests : BunitContext
{
    [Fact(DisplayName = "reports-auth-12: creates a report from nothing")]
    public void CreateReport()
    {
        ReportDraft? created = null;
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Created, value => created = value));
        cut.Find("button").Click();
        Assert.NotNull(created);
        Assert.Equal("", created.Key);
        Assert.Empty(created.Measures);
        Assert.Empty(created.Parameters);
    }
    [Fact(DisplayName = "reports-auth-13 reports-bound-9: resolves live measure options on every open")]
    public async Task ResolvesOnOpen()
    {
        var options = new List<MeasureOption> { new("finance.balance", "Balance") };
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, ReportDraft.Empty).Add(x => x.ResolveMeasures, _ => Task.FromResult<IReadOnlyList<MeasureOption>>(options.ToArray())));
        await cut.Find("button[aria-label='Choose measure']").ClickAsync();
        Assert.Contains("Balance", cut.Markup);
        await cut.Find("button[aria-label='Close measure picker']").ClickAsync();
        options.Add(new("finance.rent", "Rent"));
        await cut.Find("button[aria-label='Choose measure']").ClickAsync();
        Assert.Contains("Rent", cut.Markup);
    }
    [Fact(DisplayName = "reports-auth-8 reports-auth-9: authors typed parameters and as-at")]
    public void AuthorsParameter()
    {
        ReportDraft? changed = null;
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, ReportDraft.Empty).Add(x => x.ValueChanged, value => changed = value));
        cut.Find("[aria-label='Parameter name']").Change("as_at");
        cut.Find("[aria-label='Parameter type']").Change("date_time");
        cut.Find("[aria-label='Parameter default']").Change("2026-01-01T00:00:00+00:00");
        cut.Find("[aria-label='Effective-dated type']").Change("lease");
        cut.Find("[aria-label='Add parameter']").Click();
        var item = Assert.Single(changed!.Parameters);
        Assert.Equal("as_at", item.Name);
        Assert.Equal("date_time", item.Type);
        Assert.Equal("datetime", item.Default.Kind);
        Assert.Equal("lease", item.EffectiveDatedType);
    }

    [Fact(DisplayName = "reports-bound-1: binds the selected measure")]
    public async Task BindsMeasure()
    {
        ReportDraft? changed = null;
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, ReportDraft.Empty).Add(x => x.ValueChanged, value => changed = value).Add(x => x.ResolveMeasures, _ => Task.FromResult<IReadOnlyList<MeasureOption>>([new("finance.rent", "Rent")])));
        await cut.Find("[aria-label='Choose measure']").ClickAsync();
        cut.Find("[aria-label='Measures']").Change("finance.rent");
        await cut.Find("[aria-label='Add measure']").ClickAsync();
        Assert.Equal("finance.rent", Assert.Single(changed!.Measures).Measure);
    }

    [Fact(DisplayName = "reports-auth-15 reports-auth-22: displays located producer refusals")]
    public void LocatedRefusals()
    {
        var value = ReportDraft.Empty with { Measures = [new("finance.missing")], Parameters = [new("bad", "string", new("string", "", "string", true))] };
        var refusals = new[] { new ReportRefusal("report.measure_unknown", "/measures/0/measure", "finance.missing", "Main catalogue"), new ReportRefusal("report.parameter_default_type_mismatch", "/parameters/0") };
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, value).Add(x => x.Refusals, refusals));
        Assert.Contains("report.measure_unknown finance.missing Main catalogue", cut.Find("[aria-label='Measure 1 refusal']").TextContent);
        Assert.Contains("report.parameter_default_type_mismatch", cut.Find("[aria-label='Parameter 1 refusal']").TextContent);
    }
    [Fact(DisplayName = "reports-auth-8 reports-auth-9 reports-bound-1: authors the shared definition")]
    public async Task AuthorsFixture()
    {
        var current = new ReportDraft("tenant-r", "report-a", "1.0.0", [], []);
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, current).Add(x => x.ValueChanged, value => current = value).Add(x => x.ResolveMeasures, _ => Task.FromResult<IReadOnlyList<MeasureOption>>([new("finance.rent", "finance.rent")])));
        foreach (var row in new[] { ("enabled", "boolean", "true", ""), ("as_at", "date_time", "2026-01-01T00:00:00+00:00", "lease"), ("count", "integer", "42", ""), ("ratio", "float", "2.5", ""), ("label", "string", "hello", "") })
        {
            cut.Find("[aria-label='Parameter name']").Change(row.Item1);
            cut.Find("[aria-label='Parameter type']").Change(row.Item2);
            cut.Find("[aria-label='Parameter default']").Change(row.Item3);
            if (row.Item4.Length > 0) cut.Find("[aria-label='Effective-dated type']").Change(row.Item4);
            await cut.Find("[aria-label='Add parameter']").ClickAsync();
            cut.Render(p => p.Add(x => x.Value, current));
        }
        await cut.Find("[aria-label='Choose measure']").ClickAsync();
        cut.Find("[aria-label='Measures']").Change("finance.rent");
        await cut.Find("[aria-label='Add measure']").ClickAsync();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "conformance/hlp.ui.report-authoring/definition.json"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Shared fixture not found.");
        var expected = SharedFixtureBatch.Current is { } raw ? JsonNode.Parse(raw)!["expected"] : JsonNode.Parse(File.ReadAllText(Path.Combine(directory.FullName, "conformance/hlp.ui.report-authoring/definition.json")));
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(current, options)));
    }

    [Fact(DisplayName = "reports-auth-22: refuses an untyped default")]
    public void RefusesUntypedDefault()
    {
        var changed = false;
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, ReportDraft.Empty).Add(x => x.ValueChanged, _ => changed = true));
        cut.Find("[aria-label='Parameter name']").Change("bad");
        cut.Find("[aria-label='Parameter type']").Change("integer");
        cut.Find("[aria-label='Parameter default']").Change("not a number");
        cut.Find("[aria-label='Add parameter']").Click();
        Assert.False(changed);
        Assert.Contains("Enter a valid default", cut.Find("[role='alert']").TextContent);
    }

    [Fact(DisplayName = "reports-bound-9: empty resolver result is an empty picker")]
    public async Task EmptyPicker()
    {
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, ReportDraft.Empty).Add(x => x.ResolveMeasures, _ => Task.FromResult<IReadOnlyList<MeasureOption>>([])));
        await cut.Find("[aria-label='Choose measure']").ClickAsync();
        Assert.Empty(cut.FindAll("[aria-label='Measures'] option"));
        Assert.Empty(cut.FindAll("[role='alert']"));
    }

    [Fact(DisplayName = "readOnly prevents report changes")]
    public void ReadOnly()
    {
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, ReportDraft.Empty).Add(x => x.ReadOnly, true));
        Assert.True(cut.Find("[aria-label='Parameter name']").HasAttribute("disabled"));
        Assert.True(cut.Find("[aria-label='Add parameter']").HasAttribute("disabled"));
    }
    [Fact(DisplayName = "pending edits are retained until identity or revision replacement is decided")]
    public void PendingRevision()
    {
        var first = new ReportDraft("t", "old", "1", [new("finance.old")], []);
        var next = new ReportDraft("t", "new", "2", [new("finance.new")], []);
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, first));
        cut.Find("[aria-label='Parameter name']").Change("pending");
        cut.Render(p => p.Add(x => x.Value, next));
        Assert.Contains("finance.old", cut.Markup);
        Assert.Equal("pending", cut.Find("[aria-label='Parameter name']").GetAttribute("value"));
        Assert.True(cut.Find("[aria-label='Add parameter']").HasAttribute("disabled"));
        cut.Find("[aria-label='Discard edits and load']").Click();
        Assert.Contains("finance.new", cut.Markup);
        Assert.Equal("", cut.Find("[aria-label='Parameter name']").GetAttribute("value"));
    }

    [Fact(DisplayName = "reports-bound-9: reports resolver failures")]
    public async Task ResolverFailure()
    {
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, ReportDraft.Empty).Add(x => x.ResolveMeasures, _ => Task.FromException<IReadOnlyList<MeasureOption>>(new InvalidOperationException())));
        await cut.Find("[aria-label='Choose measure']").ClickAsync();
        Assert.Contains("Measures could not be loaded", cut.Find("[role='alert']").TextContent);
    }
    [Fact(DisplayName = "reports-auth-8 reports-bound-1: removes a parameter and a measure")]
    public void RemovesDeclarations()
    {
        ReportDraft? changed = null;
        var value = ReportDraft.Empty with { Measures = [new("finance.rent")], Parameters = [new("p", "string", new("string", "x", "string", true))] };
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, value).Add(x => x.ValueChanged, result => changed = result));
        cut.Find("[aria-label='Remove parameter p']").Click();
        Assert.Empty(changed!.Parameters);
        cut.Find("[aria-label='Remove measure finance.rent']").Click();
        Assert.Empty(changed!.Measures);
    }

    [Fact(DisplayName = "reports-bound-9: query changes call the host resolver")]
    public async Task QueryRefresh()
    {
        var last = "";
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, ReportDraft.Empty).Add(x => x.ResolveMeasures, query => { last = query; return Task.FromResult<IReadOnlyList<MeasureOption>>([]); }));
        await cut.Find("[aria-label='Choose measure']").ClickAsync();
        await cut.Find("[aria-label='Search measures']").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "rent" });
        Assert.Equal("rent", last);
    }
    [Theory(DisplayName = "reports-auth-8 reports-auth-9 reports-bound-1: shared conformance fixture"), Trait("ModuleConformance", "hlp.ui.report-authoring")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.report-authoring", MemberType = typeof(SharedFixtureBatch))]
    public Task SharedFixture(string caseId, string? rawFixture) => SharedFixtureBatch.RunAsync(caseId, rawFixture, AuthorsFixture);

    [Fact(DisplayName = "the report screen renders without a host shell")]
    public void ScreenFallback()
    {
        var cut = Render<HarborlineReportAuthoringScreen>();
        Assert.Contains("Create report", cut.Markup);
    }
    [Fact(DisplayName = "reports-bound-9: host string options use their path as label")]
    public async Task StringOption()
    {
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, ReportDraft.Empty).Add(x => x.ResolveMeasures, _ => Task.FromResult<IReadOnlyList<MeasureOption>>(["finance.rent"])));
        await cut.Find("[aria-label='Choose measure']").ClickAsync();
        Assert.Equal("finance.rent", cut.Find("[aria-label='Measures'] option").TextContent);
    }
    [Fact(DisplayName = "identity replacement closes the old measure picker")]
    public async Task DiscardClosesPicker()
    {
        var value = new ReportDraft("t", "old", "1", [], []);
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, value).Add(x => x.ResolveMeasures, _ => Task.FromResult<IReadOnlyList<MeasureOption>>(["finance.old"])));
        cut.Find("[aria-label='Parameter name']").Change("pending");
        await cut.Find("[aria-label='Choose measure']").ClickAsync();
        cut.Render(p => p.Add(x => x.Value, value with { Key = "new" }));
        cut.Find("[aria-label='Discard edits and load']").Click();
        Assert.Empty(cut.FindAll("[aria-label='Measures']"));
    }
    [Fact(DisplayName = "pending replacement hides incoming revision refusals")]
    public void HidesIncomingRefusals()
    {
        var value = new ReportDraft("t", "r", "1", [new("finance.old")], []);
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, value));
        cut.Find("[aria-label='Parameter name']").Change("pending");
        cut.Render(p => p.Add(x => x.Value, value with { Version = "2", Measures = [new("finance.new")] }).Add(x => x.Refusals, [new("report.measure_unknown", "/measures/0/measure", "finance.new", "Main")]));
        Assert.Empty(cut.FindAll("[aria-label='Measure 1 refusal']"));
    }
    [Fact(DisplayName = "keeping pending edits retains the old revision and accumulates changes")]
    public void KeepsPendingRevision()
    {
        var value = new ReportDraft("t", "r", "1", [], []);
        var changes = new List<ReportDraft>();
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, value).Add(x => x.ValueChanged, item => changes.Add(item)));
        cut.Find("[aria-label='Parameter name']").Change("first");
        cut.Render(p => p.Add(x => x.Value, value with { Version = "2" }));
        cut.Find("[aria-label='Keep editing']").Click();
        cut.Find("[aria-label='Parameter default']").Change("true");
        cut.Find("[aria-label='Add parameter']").Click();
        cut.Find("[aria-label='Parameter name']").Change("second");
        cut.Find("[aria-label='Add parameter']").Click();
        Assert.Equal("1", changes[^1].Version);
        Assert.Equal(new[] { "first", "second" }, changes[^1].Parameters.Select(p => p.Name));
    }

    [Fact(DisplayName = "readOnly disables pending replacement decisions")]
    public void ReadOnlyPending()
    {
        var value = new ReportDraft("t", "r", "1", [], []);
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, value));
        cut.Find("[aria-label='Parameter name']").Change("pending");
        cut.Render(p => p.Add(x => x.Value, value with { Version = "2" }).Add(x => x.ReadOnly, true));
        Assert.True(cut.Find("[aria-label='Keep editing']").HasAttribute("disabled"));
        Assert.True(cut.Find("[aria-label='Discard edits and load']").HasAttribute("disabled"));
    }
    [Fact(DisplayName = "a new host revision of the same definition waits for pending edits")]
    public void SameDefinitionNewRevision()
    {
        var value = new ReportDraft("t", "r", "1.0.0", [], []);
        var cut = Render<HarborlineReportAuthoringEditor>(p => p.Add(x => x.Value, value).Add(x => x.Revision, "1"));
        cut.Find("[aria-label='Parameter name']").Change("pending");
        cut.Render(p => p.Add(x => x.Revision, "2"));
        Assert.True(cut.Find("[aria-label='Add parameter']").HasAttribute("disabled"));
        cut.Find("[aria-label='Discard edits and load']").Click();
        Assert.Equal("", cut.Find("[aria-label='Parameter name']").GetAttribute("value"));
    }
    [Fact(DisplayName = "a created draft fences a later host revision when edits are pending")]
    public void CreatedDraftRevision()
    {
        var value = new ReportDraft("t", "r", "1.0.0", [], []);
        var cut = Render<HarborlineReportAuthoringEditor>();
        cut.Render(p => p.Add(x => x.Value, value).Add(x => x.Revision, "1"));
        cut.Find("[aria-label='Parameter name']").Change("pending");
        cut.Render(p => p.Add(x => x.Revision, "2"));
        Assert.True(cut.Find("[aria-label='Add parameter']").HasAttribute("disabled"));
    }
}
