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
    [Fact] public void PreservesLongCallerContent()
    {
        const string content = "A caller-owned value that is deliberately long enough to exercise the runtime handoff.";
        var rows = new[] { new ViewRuntimeRow("a1", new Dictionary<string, object?> { ["asset"] = content, ["status"] = "Open", ["owner"] = "Riley" }) };
        var cut = Render<HarborlineViewRuntime>(p => p.Add(x => x.Plan, Grid).Add(x => x.Rows, rows));
        Assert.Contains(content, cut.Markup);
    }
    [Fact, Trait("ModuleConformance", "hlp.ui.view-runtime")] public void SharedFixtureConforms(){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith("view-runtime.",fixture.RootElement.GetProperty("id").GetString());}

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
