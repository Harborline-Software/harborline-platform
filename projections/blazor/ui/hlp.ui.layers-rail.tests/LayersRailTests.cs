using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.Foundation.Builder;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class LayersRailTests : BunitContext
{
    public LayersRailTests()=>JSInterop.Mode=JSRuntimeMode.Loose;
    private static readonly IReadOnlyList<AspectLens> Lenses=[new("layout","Layout",LensTone.Accent,AspectLensKind.Colorize,_=>new(true)),new("rules","Rules",LensTone.Warning,AspectLensKind.Filter,id=>new(id=="child"))];
    private static readonly IReadOnlyList<CanvasNode> Nodes=[new("root","group","Root",0,null,HasChildren:true),new("child","field","Child",1,"root")];
    private IRenderedComponent<HarborlineLayersRail> RenderRail(Action<LayersState>? changed=null,Action<string>? selected=null)=>Render<HarborlineLayersRail>(p=>p.Add(x=>x.Lenses,Lenses).Add(x=>x.State,LayersState.Initialize(Lenses.Select(x=>x.Id),"layout")).Add(x=>x.StateChanged,state=>changed?.Invoke(state)).Add(x=>x.Canvas,new CanvasModel(Nodes,"child",selected??(_=>{}))));
    [Fact] public void StructureAndTreeLevelsMatch(){var cut=RenderRail();Assert.Equal("complementary",cut.Find("aside").LocalName=="aside"?"complementary":"missing");Assert.Equal(2,cut.FindAll("[role=treeitem]").Count);Assert.Equal("2",cut.FindAll("[role=treeitem]")[1].GetAttribute("aria-level"));}
    [Fact] public void LensAndSelectionCallbacksRemainHostOwned(){LayersState? state=null;var selected=new List<string>();var cut=RenderRail(next=>state=next,selected.Add);cut.FindAll(".hl-layers-rail__lens-activate")[1].Click();Assert.Equal("rules",state?.ActiveId);cut.Find("[data-node-id=child]").Click();Assert.Equal(["child"],selected);}
    [Theory,Trait("ModuleConformance","hlp.ui.layers-rail")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.layers-rail", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixturePrefix("layers-rail.");
            var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return;
            using var fixture = System.Text.Json.JsonDocument.Parse(raw);
            AssertCase(fixture.RootElement.GetProperty("id").GetString()!, fixture.RootElement.GetProperty("input"), fixture.RootElement.GetProperty("expected"));
        });
    }
    // Targeted neutral-case semantics: each case runs the production component against the fixture's own
    // input and expected values. tree-keyboard and collapse-focus follow the module's focus requests
    // (bUnit has no real focus; the Chromium probe checks where focus lands); compact's width is the
    // gallery's check. projection-equivalence compares lanes. An unknown case id fails.
    private static readonly string[] Unasserted = ["layers-rail.projection-equivalence"];
    private static string[] Strings(System.Text.Json.JsonElement value) => value.EnumerateArray().Select(v => v.GetString()!).ToArray();
    private static IReadOnlyList<AspectLens> LensesFor(IEnumerable<string> ids) => ids.Select(id => new AspectLens(id, char.ToUpperInvariant(id[0]) + id[1..], LensTone.Accent, AspectLensKind.Colorize, _ => new(true))).ToArray();
    private static LayersState StateFor(string? active, IEnumerable<string>? enabled = null)
    {
        var state = LayersState.Initialize([], null);
        foreach (var id in enabled ?? []) if (id != active) state = state.Toggle(id);
        return active is null ? state : state.Activate(active);
    }
    private IRenderedComponent<LayersRailHost> Host(Action<ComponentParameterCollectionBuilder<LayersRailHost>>? configure = null) => Render<LayersRailHost>(p => configure?.Invoke(p));
    private static string? Label(AngleSharp.Dom.IElement? element) => element?.QuerySelector(".hl-layers-rail__node-label")?.TextContent;
    // The treeitem is the focus target (APG treeview); its buttons are pointer-only.
    private static string Item(string id) => $"[role=treeitem][data-node-id={id}]";
    private static AngleSharp.Dom.IElement[] TabStops(IRenderedComponent<LayersRailHost> cut) => cut.FindAll("[role=tree] *").Where(e => e.GetAttribute("tabindex") is { } t
        ? int.Parse(t, System.Globalization.CultureInfo.InvariantCulture) >= 0
        : e.LocalName is "button" or "a" or "input" or "select" or "textarea").ToArray();
    private static object? State(AngleSharp.Dom.IElement? element) => element is null ? null : new
    {
        Role = element.GetAttribute("role"), Label = Label(element), Level = int.Parse(element.GetAttribute("aria-level") ?? "0", System.Globalization.CultureInfo.InvariantCulture),
        Expanded = element.GetAttribute("aria-expanded") is { } e ? e == "true" : (bool?)null, Selected = element.GetAttribute("aria-selected") == "true",
    };
    private static object ExpectedState(System.Text.Json.JsonElement e) => new
    {
        Role = e.GetProperty("role").GetString(), Label = e.GetProperty("label").GetString(), Level = e.GetProperty("level").GetInt32(),
        Expanded = e.GetProperty("expanded").ValueKind == System.Text.Json.JsonValueKind.Null ? (bool?)null : e.GetProperty("expanded").GetBoolean(), Selected = e.GetProperty("selected").GetBoolean(),
    };

    private void AssertCase(string id, System.Text.Json.JsonElement input, System.Text.Json.JsonElement expected)
    {
        var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/layers-rail.js"); module.Mode = JSRuntimeMode.Loose;
        switch (id)
        {
            case "layers-rail.structure":
            {
                var lensCount = input.GetProperty("lenses").GetInt32(); var nodeCount = input.GetProperty("nodes").GetInt32();
                var nodes = new List<CanvasNode> { new("n0", "section", "N0", 0, null, HasChildren: nodeCount > 1) };
                nodes.AddRange(Enumerable.Range(1, nodeCount - 1).Select(i => new CanvasNode($"n{i}", "field", $"N{i}", 1, "n0")));
                var cut = Host(p => p.Add(x => x.Lenses, LensesFor(new[] { "layout", "rules" }.Take(lensCount))).Add(x => x.Nodes, nodes));
                Assert.Equal(expected.GetProperty("region").GetString(), cut.Find("aside").GetAttribute("aria-label"));
                Assert.Equal(expected.GetProperty("lensGroup").GetBoolean(), cut.FindAll("[role=group]").Count > 0);
                Assert.Equal(expected.GetProperty("tree").GetBoolean(), cut.FindAll("[role=tree]").Count > 0);
                var order = cut.FindAll("[role=group], [role=tree]").Select(e => e.GetAttribute("role") == "group" ? "lenses" : "outline").ToArray();
                Assert.Equal(Strings(expected.GetProperty("order")), order);
                break;
            }
            case "layers-rail.lenses":
            {
                var ids = Strings(input.GetProperty("lensIds"));
                var cut = Host(p => p.Add(x => x.Lenses, LensesFor(ids)).Add(x => x.Initial, StateFor(input.GetProperty("activeId").GetString(), Strings(input.GetProperty("enabledIds")))));
                Assert.Equal(Strings(expected.GetProperty("activePressed")), ids.Where((_, i) => cut.FindAll(".hl-layers-rail__lens-activate")[i].GetAttribute("aria-pressed") == "true").ToArray());
                Assert.Equal(Strings(expected.GetProperty("enabledPressed")), ids.Where((_, i) => cut.FindAll(".hl-layers-rail__lens-toggle")[i].GetAttribute("aria-pressed") == "true").ToArray());
                break;
            }
            case "layers-rail.active-status":
            {
                var cut = Host(p => p.Add(x => x.Initial, StateFor(input.GetProperty("activeId").GetString())).Add(x => x.Compact, input.GetProperty("compact").GetBoolean()));
                var status = cut.Find("[role=status]");
                Assert.Equal(expected.GetProperty("status").GetString(), status.TextContent.Trim());
                Assert.Equal(expected.GetProperty("live").GetString(), status.GetAttribute("aria-live"));
                Assert.Equal(expected.GetProperty("exitPresent").GetBoolean(), cut.FindAll(".hl-layers-rail__status-close").Count > 0);
                break;
            }
            case "layers-rail.toggle":
            {
                var active = input.GetProperty("activeId").GetString(); var toggled = input.GetProperty("toggle").GetString()!;
                var cut = Host(p => p.Add(x => x.Initial, StateFor(active)));
                var index = LayersRailHost.BaseLenses.ToList().FindIndex(l => l.Id == toggled);
                var before = cut.FindAll(".hl-layers-rail__lens-toggle")[index].GetAttribute("aria-pressed");
                cut.FindAll(".hl-layers-rail__lens-toggle")[index].Click();
                Assert.Equal(expected.GetProperty("enabledChanged").GetString(), cut.FindAll(".hl-layers-rail__lens-toggle")[index].GetAttribute("aria-pressed") != before ? toggled : null);
                var activeAfter = LayersRailHost.BaseLenses.Where((_, i) => cut.FindAll(".hl-layers-rail__lens-activate")[i].GetAttribute("aria-pressed") == "true").Select(l => l.Id).FirstOrDefault();
                Assert.Equal(expected.GetProperty("activeId").GetString(), activeAfter);
                Assert.Equal(expected.GetProperty("activationCount").GetInt32(), activeAfter == active ? 0 : 1);
                break;
            }
            case "layers-rail.shortcuts":
            {
                var ids = Strings(input.GetProperty("lensIds"));
                var cut = Host(p => p.Add(x => x.Lenses, LensesFor(ids)).Add(x => x.Initial, StateFor(null)));
                string? Active() => ids.Where((_, i) => cut.FindAll(".hl-layers-rail__lens-activate")[i].GetAttribute("aria-pressed") == "true").FirstOrDefault();
                var states = Strings(input.GetProperty("keys")).Select(key => { cut.Find("[role=group]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = key }); return Active(); }).ToArray();
                Assert.Equal(expected.GetProperty("states").EnumerateArray().Select(v => v.ValueKind == System.Text.Json.JsonValueKind.Null ? null : v.GetString()).ToArray(), states);
                // Scoped: the shortcut handler is on the lens group, not the tree or the document.
                static bool HasKeyHandler(AngleSharp.Dom.IElement element) => element.Attributes.Any(a => a.Name.Equals("blazor:onkeydown", StringComparison.OrdinalIgnoreCase));
                Assert.Equal(expected.GetProperty("scopedToRail").GetBoolean(), HasKeyHandler(cut.Find("[role=group]")) && !HasKeyHandler(cut.Find("aside")) && !HasKeyHandler(cut.Find("[role=tree]")));
                break;
            }
            case "layers-rail.compact":
            {
                var cut = Host(p => p.Add(x => x.Compact, input.GetProperty("compact").GetBoolean()).Add(x => x.OnInsert, () => { }));
                Assert.Equal(expected.GetProperty("textHeadingsVisible").GetBoolean(), cut.FindAll("h2").Count > 0);
                Assert.Equal(expected.GetProperty("accessibleNamesPresent").GetBoolean(), cut.FindAll("[role=group], [role=tree], button").All(e => !string.IsNullOrWhiteSpace(e.GetAttribute("aria-label") ?? e.TextContent)));
                Assert.Equal(expected.GetProperty("width").GetString(), cut.Find("aside").ClassList.Contains("hl-layers-rail--compact") ? "compact" : "full");
                break;
            }
            case "layers-rail.outline":
            {
                var nodes = input.GetProperty("nodes").EnumerateArray().Select(n => new CanvasNode(n.GetProperty("id").GetString()!, "field", n.GetProperty("id").GetString()!, n.GetProperty("depth").GetInt32(), n.GetProperty("parentId").ValueKind == System.Text.Json.JsonValueKind.Null ? null : n.GetProperty("parentId").GetString(), HasChildren: n.TryGetProperty("hasChildren", out var h) ? h.GetBoolean() : null)).ToArray();
                var cut = Host(p => p.Add(x => x.Nodes, nodes));
                Assert.Equal(expected.GetProperty("role").GetString() == "tree", cut.FindAll("[role=tree] [role=treeitem]").Count == nodes.Length);
                Assert.Equal(expected.GetProperty("levels").EnumerateArray().Select(v => v.GetInt32()).ToArray(), cut.FindAll("[role=treeitem]").Select(t => int.Parse(t.GetAttribute("aria-level")!, System.Globalization.CultureInfo.InvariantCulture)).ToArray());
                Assert.Equal(expected.GetProperty("expanded").GetBoolean(), cut.FindAll("[role=treeitem]")[0].GetAttribute("aria-expanded") == "true");
                break;
            }
            case "layers-rail.tree-keyboard":
            {
                var cut = Host();
                var current = "root";
                var visits = new List<string>();
                foreach (var key in Strings(input.GetProperty("keys")))
                {
                    var before = module.Invocations["focusNode"].Count;
                    cut.Find(Item(current)).KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = key });
                    if (module.Invocations["focusNode"].Count > before) current = (string)module.Invocations["focusNode"].Last().Arguments[1]!;
                    visits.Add(current);
                }
                // Down → child, Up → root, Right on an expanded parent → child, Left on a leaf → root, Home → root, End → sibling.
                Assert.Equal(["ArrowDown", "ArrowUp", "ArrowRight", "ArrowLeft", "Home", "End"], Strings(input.GetProperty("keys")));
                Assert.Equal(expected.GetProperty("apgMovement").GetBoolean(), visits.SequenceEqual(["child", "root", "child", "root", "root", "sibling"]));
                var stops = TabStops(cut);
                Assert.Equal(expected.GetProperty("rovingTabIndex").GetBoolean(), stops.Length == 1 && stops[0].GetAttribute("data-node-id") == current);
                Assert.Equal(expected.GetProperty("tabStopRole").GetString(), stops.FirstOrDefault()?.GetAttribute("role"));
                // The module focuses that tab stop itself.
                Assert.Equal(stops[0].OuterHtml, LayersRailScript.FocusTarget(cut.Find("aside"), current)?.OuterHtml);
                break;
            }
            case "layers-rail.collapse-focus":
            {
                var cut = Host();
                cut.Find(Item(input.GetProperty("focus").GetString()!)).Focus();
                cut.Find($"[data-node-id={input.GetProperty("collapse").GetString()}] .hl-layers-rail__disclosure").Click();
                Assert.Equal(Strings(expected.GetProperty("visibleIds")), cut.FindAll("[role=treeitem]").Select(t => t.GetAttribute("data-node-id")!).Where(n => n != "sibling").ToArray());
                var requested = module.Invocations["focusNode"].Select(c => (string?)c.Arguments[1]).LastOrDefault();
                Assert.Equal(expected.GetProperty("focusedId").GetString(), requested);
                Assert.Equal(expected.GetProperty("focusedRole").GetString(), requested is null ? null : LayersRailScript.FocusTarget(cut.Find("aside"), requested)?.GetAttribute("role"));
                Assert.Equal(expected.GetProperty("bodyFocused").GetBoolean(), requested is null);
                break;
            }
            case "layers-rail.treeitem-focus":
            {
                var cut = Host(p => p.Add(x => x.Selected, input.GetProperty("selectedId").GetString()));
                var stops = TabStops(cut);
                Assert.Equal(expected.GetProperty("tabStops").GetInt32(), stops.Length);
                Assert.Equal(expected.GetProperty("tabStopRole").GetString(), stops[0].GetAttribute("role"));
                // Tab lands on the one stop; each key then moves focus where the module's focus request points.
                AngleSharp.Dom.IElement focused = stops[0];
                var visits = new List<object?> { State(focused) };
                foreach (var key in Strings(input.GetProperty("keys")))
                {
                    var before = module.Invocations["focusNode"].Count;
                    focused.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = key });
                    if (module.Invocations["focusNode"].Count > before) focused = LayersRailScript.FocusTarget(cut.Find("aside"), (string)module.Invocations["focusNode"].Last().Arguments[1]!)!;
                    visits.Add(State(focused));
                    // The focused element is the roving tab stop.
                    Assert.Equal("0", focused.GetAttribute("tabindex"));
                }
                Assert.Equal(expected.GetProperty("focused").EnumerateArray().Select(ExpectedState).ToArray(), visits.ToArray());
                // The accessible name is the label element alone (aria-labelledby), not the disclosure or provenance text.
                var labelledBy = focused.GetAttribute("aria-labelledby");
                var nameElement = labelledBy is null ? null : cut.FindAll("[id]").SingleOrDefault(e => e.Id == labelledBy);
                Assert.Equal(expected.GetProperty("accessibleNameIsLabel").GetBoolean(), nameElement is not null && nameElement.ClassList.Contains("hl-layers-rail__node-label") && focused.Contains(nameElement) && nameElement.TextContent == Label(focused));
                break;
            }
            case "layers-rail.selection":
            {
                var selected = new List<string>();
                var cut = Host(p => p.Add(x => x.OnSelect, selected.Add));
                cut.Find($"[data-node-id={input.GetProperty("activateNode").GetString()}] .hl-layers-rail__node-control").Click();
                Assert.Equal(Strings(expected.GetProperty("selectedIds")), cut.FindAll("[role=treeitem][aria-selected=true]").Select(t => t.GetAttribute("data-node-id")!).ToArray());
                Assert.Equal(expected.GetProperty("callbackCount").GetInt32(), selected.Count);
                // The click hands focus to the selected row's treeitem.
                var requested = module.Invocations["focusNode"].Select(c => (string?)c.Arguments[1]).LastOrDefault();
                Assert.Equal(input.GetProperty("activateNode").GetString(), requested);
                Assert.Equal(expected.GetProperty("focusedRole").GetString(), requested is null ? null : LayersRailScript.FocusTarget(cut.Find("aside"), requested)?.GetAttribute("role"));
                break;
            }
            case "layers-rail.filter-badges":
            {
                var inactive = input.GetProperty("inactiveNode").GetString();
                var kind = input.GetProperty("activeKind").GetString() == "filter" ? AspectLensKind.Filter : AspectLensKind.Colorize;
                IReadOnlyList<AspectLens> lenses = [new("rules", "Rules", LensTone.Warning, kind, n => new(n != inactive)), new("access", "Access", LensTone.Danger, AspectLensKind.Colorize, n => new(n != inactive))];
                var cut = Host(p => p.Add(x => x.Lenses, lenses).Add(x => x.Initial, StateFor("rules", ["rules", "access"])));
                var child = cut.FindAll($"[data-node-id={inactive}]").FirstOrDefault();
                Assert.Equal(expected.GetProperty("childPresent").GetBoolean(), child is not null);
                Assert.Equal(expected.GetProperty("ghosted").GetBoolean(), child!.GetAttribute("data-hl-ghosted") == "true");
                var passive = cut.FindAll(".hl-layers-rail__lens-activate")[1].QuerySelector(".hl-layers-rail__count")?.TextContent ?? string.Empty;
                Assert.Equal(expected.GetProperty("passiveText").GetString(), passive);
                Assert.Equal($"+{input.GetProperty("passiveMatches").GetInt32()}", passive);
                Assert.Equal(expected.GetProperty("colorOnly").GetBoolean(), !(child.HasAttribute("data-hl-ghosted") && passive.StartsWith('+')));
                break;
            }
            case "layers-rail.provenance":
            {
                var resolved = input.GetProperty("resolved"); var unresolved = input.GetProperty("unresolved");
                static ProvenanceSource Source(string value) => Enum.Parse<ProvenanceSource>(value, true);
                var cut = Host(p => p.Add(x => x.Provenance, new ProvenanceResolver(n => n == "child"
                    ? new(Source(unresolved.GetProperty("source").GetString()!), [], false, unresolved.GetProperty("locked").GetBoolean(), false)
                    : new(Source(resolved.GetProperty("source").GetString()!), [], false, resolved.GetProperty("locked").GetBoolean(), true))));
                Assert.Equal(expected.GetProperty("resolvedText").GetString(), cut.Find("[data-node-id=root] .hl-layers-rail__source").TextContent);
                var text = cut.Find("[data-node-id=child] .hl-layers-rail__source").TextContent;
                Assert.Equal(expected.GetProperty("unresolvedFailsClosed").GetBoolean(), text.StartsWith(DefaultRailLabels.Value.UnresolvedSource, StringComparison.Ordinal) && text.Contains(DefaultRailLabels.Value.Locked, StringComparison.Ordinal) == unresolved.GetProperty("locked").GetBoolean());
                break;
            }
            case "layers-rail.insert":
            {
                var calls = 0;
                var cut = Host(p => { if (input.GetProperty("hasCallback").GetBoolean()) p.Add(x => x.OnInsert, () => calls++); });
                var insert = cut.Find(".hl-layers-rail__insert");
                Assert.Equal(expected.GetProperty("accessibleName").GetString(), insert.GetAttribute("aria-label"));
                for (var i = 0; i < input.GetProperty("activations").GetInt32(); i++) cut.Find(".hl-layers-rail__insert").Click();
                Assert.Equal(expected.GetProperty("callbackCount").GetInt32(), calls);
                break;
            }
            case "layers-rail.empty":
            {
                var hint = input.GetProperty("activeLensEmpty").GetString();
                IReadOnlyList<AspectLens> lenses = [LayersRailHost.BaseLenses[1] with { Empty = hint }];
                var withLens = Host(p => p.Add(x => x.Nodes, Array.Empty<CanvasNode>()).Add(x => x.Lenses, lenses).Add(x => x.Initial, StateFor("rules")));
                Assert.Equal(expected.GetProperty("lensHintWhenApplicable").GetBoolean(), withLens.Find("[role=tree] p").TextContent == hint);
                var plain = Host(p => p.Add(x => x.Nodes, Array.Empty<CanvasNode>()).Add(x => x.Initial, StateFor(null)));
                Assert.Equal(expected.GetProperty("emptyVisible").GetBoolean(), plain.FindAll("[role=tree] p").Count == 1);
                break;
            }
            case "layers-rail.continuity":
            {
                var cut = Host(p => p.Add(x => x.Selected, input.GetProperty("selectedId").GetString()));
                var focusId = input.GetProperty("focusedId").GetString();
                cut.Find(Item(focusId!)).Focus();
                var index = LayersRailHost.BaseLenses.ToList().FindIndex(l => l.Id == input.GetProperty("switchLens").GetString());
                cut.FindAll(".hl-layers-rail__lens-activate")[index].Click();
                var node = cut.Find($"[data-node-id={focusId}]");
                Assert.Equal(expected.GetProperty("selectionPreserved").GetBoolean(), node.GetAttribute("aria-selected") == "true");
                Assert.Equal(expected.GetProperty("focusPreserved").GetBoolean(), node.GetAttribute("tabindex") == "0");
                Assert.Equal(expected.GetProperty("nodeIdentityPreserved").GetBoolean(), Label(node) == "Child");
                break;
            }
            case "layers-rail.invalid-input":
            {
                var errors = new List<string>();
                try { Host(p => p.Add(x => x.Lenses, LensesFor(Strings(input.GetProperty("lensIds"))))); } catch (InvalidOperationException e) { errors.Add(e.Message); }
                try { Host(p => p.Add(x => x.Nodes, Strings(input.GetProperty("nodeIds")).Select(n => new CanvasNode(n, "field", n, 0, null)).ToArray())); } catch (InvalidOperationException e) { errors.Add(e.Message); }
                Assert.Equal(2, errors.Count);
                Assert.Equal(Strings(expected.GetProperty("errors")), errors.Distinct().ToArray());
                break;
            }
            default:
                Assert.Contains(id, Unasserted);
                break;
        }
    }
    private static void AssertFixturePrefix(string prefix){var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
