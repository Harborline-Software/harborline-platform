using Microsoft.AspNetCore.Components;
using Harborline.Foundation.Builder;
using Harborline.UIAdapters.Blazor.Components.Navigation;

namespace Harborline.UIAdapters.Blazor.Tests;

// A host that owns lens and selection state the way an application would.
internal sealed class LayersRailHost : ComponentBase
{
    public static readonly IReadOnlyList<CanvasNode> BaseNodes = [new("root", "section", "Root", 0, null, HasChildren: true), new("child", "field", "Child", 1, "root"), new("sibling", "field", "Sibling", 0, null)];
    public static readonly IReadOnlyList<AspectLens> BaseLenses = [new("layout", "Layout", LensTone.Accent, AspectLensKind.Colorize, _ => new(true)), new("rules", "Rules", LensTone.Warning, AspectLensKind.Filter, id => new(id == "child", id == "child" ? "1 rule" : null), Empty: "Add a rule")];
    [Parameter] public IReadOnlyList<CanvasNode> Nodes { get; set; } = BaseNodes;
    [Parameter] public IReadOnlyList<AspectLens> Lenses { get; set; } = BaseLenses;
    [Parameter] public LayersState? Initial { get; set; }
    [Parameter] public string? Selected { get; set; }
    [Parameter] public RailLabels? Labels { get; set; }
    [Parameter] public bool Compact { get; set; }
    [Parameter] public EventCallback OnInsert { get; set; }
    [Parameter] public ProvenanceResolver? Provenance { get; set; }
    [Parameter] public Action<string>? OnSelect { get; set; }
    private LayersState? state;
    private string? selected;
    private bool initialized;
    protected override void OnParametersSet() { if (initialized) return; initialized = true; state = Initial ?? LayersState.Initialize(Lenses.Select(l => l.Id), "layout"); selected = Selected; }
    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
    {
        builder.OpenComponent<HarborlineLayersRail>(0);
        builder.AddComponentParameter(1, nameof(HarborlineLayersRail.Lenses), Lenses);
        builder.AddComponentParameter(2, nameof(HarborlineLayersRail.State), state);
        builder.AddComponentParameter(3, nameof(HarborlineLayersRail.StateChanged), EventCallback.Factory.Create<LayersState>(this, next => state = next));
        builder.AddComponentParameter(4, nameof(HarborlineLayersRail.Canvas), new CanvasModel(Nodes, selected, id => { selected = id; OnSelect?.Invoke(id); StateHasChanged(); }));
        builder.AddComponentParameter(5, nameof(HarborlineLayersRail.Provenance), Provenance ?? new ProvenanceResolver(_ => new(ProvenanceSource.Tenant, [ProvenanceSource.Tenant], false, false, true)));
        if (Labels is not null) builder.AddComponentParameter(6, nameof(HarborlineLayersRail.Labels), Labels);
        builder.AddComponentParameter(7, nameof(HarborlineLayersRail.Compact), Compact);
        if (OnInsert.HasDelegate) builder.AddComponentParameter(8, nameof(HarborlineLayersRail.OnInsert), OnInsert);
        builder.CloseComponent();
    }
}
