using Bunit;
using Harborline.UIAdapters.Blazor.Components.DataDisplay;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ViewRuntimeTests : BunitContext
{
    // The grid imports its JS module and connects on first render; the runtime test is about the mapping, so
    // bUnit answers every JS call loosely rather than scripting the grid module.
    public ViewRuntimeTests() { JSInterop.Mode = JSRuntimeMode.Loose; }
    private static readonly ViewRenderPlan Grid = new("sha256:view-assets", "view-assets", "1", "harborline.platform", "1.0.0", "ViewDefinition", new("views.entity-list/grid", new([new("asset", "Asset"), new("status", "Status"), new("owner", "Owner")]))) ;

    [Fact]
    public void Grid_exposes_the_same_pack_provenance_as_the_react_lane()
    {
        var cut = Render<HarborlineViewRuntime>(parameters => parameters
            .Add(component => component.Plan, Grid)
            .Add(component => component.Rows, Rows));

        var runtime = cut.Find(".hl-view-runtime");
        Assert.Equal(
            "{\"definitionId\":\"view-assets\",\"definitionVersion\":\"1\",\"packKey\":\"harborline.platform\"}",
            runtime.GetAttribute("data-definition-source"));
        Assert.Null(runtime.GetAttribute("data-definition-id"));
        Assert.Null(runtime.GetAttribute("data-definition-version"));
    }
    private static readonly IReadOnlyList<ViewRuntimeRow> Rows = [new("a1", new Dictionary<string, object?> { ["asset"] = "Pier", ["status"] = "Open", ["owner"] = "Riley" }), new("a2", new Dictionary<string, object?> { ["asset"] = "Pump", ["status"] = "Review", ["owner"] = "Morgan" })];
    [Fact] public void GridDefinitionMapsFieldsAndRows(){var cut=Render<HarborlineViewRuntime>(p=>p.Add(x=>x.Plan,Grid).Add(x=>x.Rows,Rows));Assert.Equal(["Asset","Status","Owner"],cut.FindAll("[role=columnheader]").Select(node=>node.TextContent));Assert.Equal(2,cut.FindAll("[data-row-id]").Count);}
    [Fact] public void UnknownKindIsInertAndSilent(){var logs=new CapturingLoggerProvider();Services.AddLogging(builder=>builder.AddProvider(logs));var cut=Render<HarborlineViewRuntime>(p=>p.Add(x=>x.Plan,Grid with { Bindings=Grid.Bindings with { ViewKind="views.unknown" } }).Add(x=>x.Rows,Rows));Assert.True(string.IsNullOrWhiteSpace(cut.Markup));Assert.Empty(logs.Entries);Assert.Empty(JSInterop.Invocations);}
    [Fact] public void EmptyRowsKeepDeclaredColumns(){var cut=Render<HarborlineViewRuntime>(p=>p.Add(x=>x.Plan,Grid).Add(x=>x.Empty,"No matching assets.").Add(x=>x.Rows,[]));Assert.Equal(3,cut.FindAll("[role=columnheader]").Count);Assert.Contains("No matching assets.",cut.Markup);}
    [Fact] public void NormalizesMissingNullAndNonStringValues(){var cut=Render<HarborlineViewRuntime>(p=>p.Add(x=>x.Plan,Grid).Add(x=>x.Rows,[new ViewRuntimeRow("a1",new Dictionary<string,object?>{{"asset",null},{"status",42}})]));Assert.Equal([string.Empty,"42",string.Empty],cut.FindAll("[role=gridcell]").Select(node=>node.TextContent));}
    [Fact] public void ForwardsTheSharedRowActivationAction(){string? rowId=null;var cut=Render<HarborlineViewRuntime>(p=>p.Add(x=>x.Plan,Grid).Add(x=>x.Rows,Rows).Add(x=>x.OnRowActivate,value=>rowId=value));cut.Find("[data-row-id=a1]").DoubleClick();Assert.Equal("a1",rowId);}
    [Fact] public void PreservesLongCallerContent()
    {
        const string content = "A caller-owned value that is deliberately long enough to exercise the runtime handoff.";
        var rows = new[] { new ViewRuntimeRow("a1", new Dictionary<string, object?> { ["asset"] = content, ["status"] = "Open", ["owner"] = "Riley" }) };
        var cut = Render<HarborlineViewRuntime>(p => p.Add(x => x.Plan, Grid).Add(x => x.Rows, rows));
        Assert.Contains(content, cut.Markup);
    }
    [Fact, Trait("ModuleConformance", "hlp.ui.view-runtime")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        var root = fixture.RootElement;
        var input = root.GetProperty("input");
        var plan = ReadPlan(input.GetProperty("plan"));
        var rows = input.TryGetProperty("rows", out var rowElements)
            ? rowElements.EnumerateArray().Select(ReadRow).ToArray()
            : [];
        var activated = new List<string>();
        var cut = Render<HarborlineViewRuntime>(p => p.Add(x => x.Plan, plan).Add(x => x.Rows, rows)
            .Add(x => x.OnAction, id => activated.Add(id))
            .Add(x => x.Empty, input.TryGetProperty("empty", out var empty) ? empty.GetString() : null));
        var expected = root.GetProperty("expected");
        if (expected.TryGetProperty("nodes", out var nodes))
        {
            Assert.Equal(nodes.GetInt32(), cut.Nodes.Length);
            Assert.Empty(JSInterop.Invocations);
            Assert.Empty(activated);
            return;
        }
        if (expected.TryGetProperty("columns", out var columns))
            Assert.Equal(columns.EnumerateArray().Select(value => value.GetString()), cut.FindAll("[role=columnheader]").Select(node => node.TextContent));
        if (expected.TryGetProperty("rowCount", out var rowCount))
            Assert.Equal(rowCount.GetInt32(), cut.FindAll("[data-row-id]").Count);
        if (expected.TryGetProperty("content", out var content))
            Assert.Contains(content.GetString()!, cut.Markup);
        var buttons = cut.FindAll("button");
        Assert.Equal(expected.TryGetProperty("actions", out var labels)
            ? labels.EnumerateArray().Select(label => label.GetString()) : [], buttons.Select(button => button.TextContent));
        if (input.TryGetProperty("activateActions", out var actions))
        {
            foreach (var id in actions.EnumerateArray())
            {
                var action = plan.Bindings.Actions!.Single(candidate => candidate.Id == id.GetString());
                var button = cut.FindAll("button").Single(candidate => candidate.TextContent == action.Label);
                Assert.Equal("button", button.GetAttribute("type"));
                button.Click();
            }
        }
        Assert.Equal(expected.TryGetProperty("activated", out var ids)
            ? ids.EnumerateArray().Select(id => id.GetString()) : [], activated);
    }

    private static ViewRenderPlan ReadPlan(System.Text.Json.JsonElement plan)
    {
        var bindings = plan.GetProperty("bindings");
        var fields = bindings.TryGetProperty("parameters", out var parameters)
            && parameters.TryGetProperty("fields", out var fieldElements)
            ? fieldElements.EnumerateArray().Select(field => new ViewDefinitionField(field.GetProperty("id").GetString()!, field.GetProperty("label").GetString())).ToArray()
            : null;
        var actions = bindings.TryGetProperty("actions", out var actionElements)
            ? actionElements.EnumerateArray().Select(action => new ViewRuntimeAction(action.GetProperty("id").GetString()!, action.GetProperty("label").GetString()!)).ToArray()
            : null;
        return new(
            plan.GetProperty("definitionHash").GetString()!, plan.GetProperty("definitionId").GetString()!,
            plan.GetProperty("definitionVersion").GetString()!, plan.GetProperty("packKey").GetString()!,
            plan.GetProperty("packVersion").GetString()!, plan.GetProperty("definitionKind").GetString()!,
            new(bindings.GetProperty("viewKind").GetString(), fields is null ? null : new(fields), actions));
    }

    [Fact]
    public void DeclaredActionsForwardTheirIdsInActivationOrder()
    {
        var actions = new[] { new ViewRuntimeAction("first", "First"), new ViewRuntimeAction("second", "Second") };
        var activated = new List<string>();
        var cut = Render<HarborlineViewRuntime>(p => p.Add(x => x.Plan, Grid with { Bindings = Grid.Bindings with { Actions = actions } })
            .Add(x => x.Rows, []).Add(x => x.OnAction, id => activated.Add(id)));
        var buttons = cut.FindAll("button");
        Assert.Equal(["First", "Second"], buttons.Select(button => button.TextContent));
        Assert.All(buttons, button => Assert.Equal("button", button.GetAttribute("type")));
        cut.FindAll("button")[1].Click();
        cut.FindAll("button")[0].Click();
        Assert.Equal(["second", "first"], activated);
    }

    [Fact]
    public void DeclaredActionWithNoHandlerIsAnInertNonSubmitButton()
    {
        var cut = Render<HarborlineViewRuntime>(p => p.Add(x => x.Plan, Grid with
            { Bindings = Grid.Bindings with { Actions = [new("action", "Act")] } }).Add(x => x.Rows, []));
        var button = cut.Find("button");
        Assert.Equal("button", button.GetAttribute("type"));
        button.Click();
    }

    private static ViewRuntimeRow ReadRow(System.Text.Json.JsonElement row) => new(
        row.GetProperty("id").GetString()!,
        row.EnumerateObject().Where(property => property.Name != "id").ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => (object?)property.Value.GetString(),
                System.Text.Json.JsonValueKind.Number => property.Value.GetInt32(),
                System.Text.Json.JsonValueKind.Null => null,
                _ => property.Value.GetRawText(),
            }));

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);
        public void Dispose() { }
    }

    private sealed class CapturingLogger(List<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => entries.Add(formatter(state, exception));
    }
}
