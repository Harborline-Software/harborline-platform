using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Harborline.UIAdapters.Blazor.Localization;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SearchInputTests : BunitContext
{
    // search-input.js observes Escape on the searchbox; tests that don't exercise it still need the import.
    private readonly BunitJSModuleInterop escapeModule;
    public SearchInputTests() { escapeModule = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/search-input.js"); escapeModule.Mode = JSRuntimeMode.Loose; }
    [Fact] public async Task CoalescesToLatestValue(){var values=new List<string>();var cut=Render<HarborlineSearchInput>(p=>p.Add(x=>x.DebounceMs,250).Add(x=>x.ValueChanged,value=>values.Add(value)));cut.Find("input").Input("o");cut.Find("input").Input("oak");await cut.WaitForAssertionAsync(()=>Assert.Equal(["oak"],values),TimeSpan.FromSeconds(5));Assert.Equal("false",cut.Find(".hl-search-input").GetAttribute("aria-busy"));}
    [Fact] public void ClearPublishesImmediately(){var values=new List<string>();var cut=Render<HarborlineSearchInput>(p=>p.Add(x=>x.Value,"oak").Add(x=>x.DebounceMs,1000).Add(x=>x.ValueChanged,value=>values.Add(value)));cut.Find("input").Input("stale");cut.Find("button").Click();Assert.Equal([string.Empty],values);Assert.Empty(cut.FindAll("button"));}
    [Fact] public void ExternalValueReplacesDraft(){var cut=Render<HarborlineSearchInput>(p=>p.Add(x=>x.Value,"old"));cut.Find("input").Input("draft");cut.Render(p=>p.Add(x=>x.Value,"saved"));Assert.Equal("saved",cut.Find("input").GetAttribute("value"));Assert.Equal("false",cut.Find(".hl-search-input").GetAttribute("aria-busy"));}
    // The clear control exists only for a nonempty draft, so activating it removes the focused
    // element; focus must return to the searchbox rather than fall to the page (WCAG 2.4.3).
    [Fact] public void ClearReturnsFocusToTheSearchbox(){JSInterop.Mode=JSRuntimeMode.Loose;var values=new List<string>();var focusCallsWhenPublished=new List<int>();var cut=Render<HarborlineSearchInput>(p=>p.Add(x=>x.Value,"oak").Add(x=>x.ValueChanged,value=>{values.Add(value);focusCallsWhenPublished.Add(JSInterop.Invocations.Count(invocation=>invocation.Identifier.Contains("focus",StringComparison.OrdinalIgnoreCase)));}));cut.Find("button.hl-search-input__clear").Click();Assert.Equal([""],values);Assert.Empty(cut.FindAll("button.hl-search-input__clear"));Assert.Equal([1],focusCallsWhenPublished);var focus=JSInterop.VerifyFocusAsyncInvoke();Assert.False(string.IsNullOrEmpty(Assert.IsType<Microsoft.AspNetCore.Components.ElementReference>(focus.Arguments[0]).Id));}
    // Blank caller text is not an accessible name (WCAG 4.1.2); the searchbox falls back to the placeholder.
    [Fact] public void BlankAccessibleLabelFallsBackToThePlaceholderName(){var cut=Render<HarborlineSearchInput>(p=>p.Add(x=>x.AccessibleLabel,"  ").Add(x=>x.Placeholder,"Find assets"));Assert.Equal("Find assets",cut.Find("input").GetAttribute("aria-label"));}
    // Contract: clearing publishes the empty string exactly once without waiting. Escape on a nonempty
    // search is a clear: search-input.js marks the key handled on the input and calls back (the Chromium
    // probe search-input-escape.mjs checks the key handling); the callback clears at once.
    [Fact] public void EscapeObserverIsRegisteredOnTheSearchbox()
    {
        var cut = Render<HarborlineSearchInput>(p => p.Add(x => x.Value, "oak").Add(x => x.ValueChanged, _ => { }));
        var observe = Assert.Single(escapeModule.Invocations["observeEscape"]);
        Assert.IsType<Microsoft.AspNetCore.Components.ElementReference>(observe.Arguments[0]);
        Assert.IsAssignableFrom<Microsoft.JSInterop.DotNetObjectReference<HarborlineSearchInput>>(observe.Arguments[1]);
    }
    [Fact] public async Task EscapeOnANonemptySearchClearsAtOnceAndCancelsThePendingPublish()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var published = new List<string>();
        var cut = Render<HarborlineSearchInput>(p => p.Add(x => x.Value, "oak").Add(x => x.DebounceMs, 10_000).Add(x => x.ValueChanged, published.Add));
        cut.Find("input").Input("oak tr");
        Assert.Equal("true", cut.Find(".hl-search-input").GetAttribute("aria-busy"));
        await cut.InvokeAsync(cut.Instance.ClearFromEscapeAsync);
        Assert.Equal([""], published);
        Assert.Equal(string.Empty, cut.Find("input").GetAttribute("value"));
        Assert.Equal("false", cut.Find(".hl-search-input").GetAttribute("aria-busy"));
        Assert.Empty(cut.FindAll("button.hl-search-input__clear"));
        JSInterop.VerifyFocusAsyncInvoke();
    }
    [Fact] public async Task EscapeOnAnEmptySearchPublishesNothing()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var published = new List<string>();
        var cut = Render<HarborlineSearchInput>(p => p.Add(x => x.Value, "").Add(x => x.ValueChanged, published.Add));
        await cut.InvokeAsync(cut.Instance.ClearFromEscapeAsync);
        Assert.Empty(published);
    }
    [Theory,Trait("ModuleConformance","hlp.ui.search-input")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.search-input", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixturePrefix("search-input.");Assert.NotNull(Render<HarborlineSearchInput>());
            var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;
            using var fixture=System.Text.Json.JsonDocument.Parse(raw);
            AssertCase(fixture.RootElement.GetProperty("id").GetString()!,fixture.RootElement.GetProperty("input"),fixture.RootElement.GetProperty("expected"));
        });
    }
    // Targeted neutral-case semantics: each case runs the production component against the
    // fixture's own input and expected values, with real debounce delays. rtl concerns logical CSS
    // edges a browser must observe; projection-equivalence compares lanes. An unknown id fails.
    private static readonly string[] Unasserted=["search-input.rtl","search-input.projection-equivalence"];
    private sealed record Harness(IRenderedComponent<HarborlineSearchInput> Cut,List<string> Published)
    {
        public AngleSharp.Dom.IElement Box=>Cut.Find("input");
        public string Draft=>Box.GetAttribute("value")??string.Empty;
        public bool Busy=>Cut.Find(".hl-search-input").GetAttribute("aria-busy")=="true";
        public bool ClearPresent=>Cut.FindAll("button.hl-search-input__clear").Count>0;
        public void Type(string value)=>Box.Input(value);
    }
    private Harness Mount(string value="",int? debounceMs=null)
    {
        JSInterop.Mode=JSRuntimeMode.Loose;var published=new List<string>();
        // DebounceMs is passed only when a case names one, so defaults exercises the component's own default.
        var cut=Render<HarborlineSearchInput>(p=>{p.Add(x=>x.Value,value).Add(x=>x.ValueChanged,v=>{lock(published)published.Add(v);});if(debounceMs is int delay)p.Add(x=>x.DebounceMs,delay);});
        return new(cut,published);
    }
    private static void Wait(int milliseconds)=>Thread.Sleep(milliseconds);
    private void AssertCase(string id,System.Text.Json.JsonElement input,System.Text.Json.JsonElement expected)
    {
        static string[] Strings(System.Text.Json.JsonElement element)=>element.EnumerateArray().Select(value=>value.GetString()!).ToArray();
        switch(id)
        {
            case "search-input.defaults":
            {
                var h=Mount(input.GetProperty("value").GetString()!);
                Assert.Equal(expected.GetProperty("placeholder").GetString(),h.Box.GetAttribute("placeholder"));
                Assert.Equal(expected.GetProperty("role").GetString(),h.Box.GetAttribute("role"));
                Assert.Equal(expected.GetProperty("clearPresent").GetBoolean(),h.ClearPresent);
                var debounce=expected.GetProperty("debounceMs").GetInt32();
                h.Type("a");Wait(debounce/2);Assert.Empty(h.Published);
                Assert.True(SpinWait.SpinUntil(()=>h.Published.Count==1,TimeSpan.FromMilliseconds(debounce*5)));
                Assert.Equal(["a"],h.Published);
                break;
            }
            case "search-input.typing":
            {
                var h=Mount(debounceMs:input.GetProperty("debounceMs").GetInt32());
                foreach(var edit in Strings(input.GetProperty("edits")))h.Type(edit);
                Assert.True(SpinWait.SpinUntil(()=>h.Published.Count>0,TimeSpan.FromMilliseconds(input.GetProperty("advanceMs").GetInt32()*5)));
                Wait(input.GetProperty("advanceMs").GetInt32());
                Assert.Equal(expected.GetProperty("draft").GetString(),h.Draft);
                Assert.Equal(Strings(expected.GetProperty("published")),h.Published.ToArray());
                break;
            }
            case "search-input.coalesce":
            {
                var debounce=input.GetProperty("debounceMs").GetInt32();var h=Mount(debounceMs:debounce);var edits=Strings(input.GetProperty("edits"));
                foreach(var edit in edits){h.Type(edit);Wait(input.GetProperty("intervalMs").GetInt32());}
                Assert.True(SpinWait.SpinUntil(()=>h.Published.Count>0,TimeSpan.FromMilliseconds(debounce*5)));
                Wait(debounce);
                Assert.Equal(Strings(expected.GetProperty("published")),h.Published.ToArray());
                Assert.Equal(expected.GetProperty("supersededCount").GetInt32(),edits.Length-h.Published.Count);
                break;
            }
            case "search-input.busy":
            {
                var h=Mount();h.Type(input.GetProperty("edit").GetString()!);
                var boundary=input.GetProperty("advanceMs").EnumerateArray().Last().GetInt32();
                var states=new List<bool>{h.Busy};
                // Still busy well before the boundary, then idle once it has passed.
                Wait(boundary/2);Assert.True(h.Busy);
                h.Cut.WaitForAssertion(()=>Assert.False(h.Busy),TimeSpan.FromMilliseconds(boundary*10));states.Add(h.Busy);
                Assert.Equal(expected.GetProperty("busyStates").EnumerateArray().Select(value=>value.GetBoolean()).ToArray(),states.ToArray());
                break;
            }
            case "search-input.external-sync":
            {
                var h=Mount();h.Type(input.GetProperty("draft").GetString()!);
                Assert.True(input.GetProperty("pending").GetBoolean()&&h.Busy);
                h.Cut.Render(p=>p.Add(x=>x.Value,input.GetProperty("externalValue").GetString()!));
                Wait(600);
                Assert.Equal(expected.GetProperty("draft").GetString(),h.Draft);
                Assert.Equal(expected.GetProperty("busy").GetBoolean(),h.Busy);
                Assert.Equal(Strings(expected.GetProperty("published")),h.Published.ToArray());
                break;
            }
            case "search-input.clear":
            {
                var h=Mount(input.GetProperty("value").GetString()!);h.Type(input.GetProperty("pending").GetString()!);
                h.Cut.Find("button.hl-search-input__clear").Click();
                Wait(600);
                Assert.Equal(expected.GetProperty("draft").GetString(),h.Draft);
                Assert.Equal(expected.GetProperty("busy").GetBoolean(),h.Busy);
                Assert.Equal(Strings(expected.GetProperty("published")),h.Published.ToArray());
                break;
            }
            case "search-input.empty":
                Assert.Equal(expected.GetProperty("clearPresent").GetBoolean(),Mount(input.GetProperty("value").GetString()!).ClearPresent);
                break;
            case "search-input.localization":
            {
                var catalog=input.GetProperty("catalog").EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.GetString()!);
                var cut=Render<HarborlineLocaleProvider>(p=>p.Add(x=>x.Catalog,catalog).AddChildContent<HarborlineSearchInput>(c=>c.Add(x=>x.Value,"oak")));
                Assert.Equal(expected.GetProperty("placeholder").GetString(),cut.Find("input").GetAttribute("placeholder"));
                Assert.Equal(expected.GetProperty("clearName").GetString(),cut.Find("button.hl-search-input__clear").GetAttribute("aria-label"));
                break;
            }
            case "search-input.catalog-copy":
            {
                // Placeholder, searchbox name, clear name and (for host catalogs) direction, read from one render.
                Dictionary<string, string?> Copy(AngleSharp.Dom.IElement root, bool withDir)
                {
                    var box = root.QuerySelector("input")!;
                    var result = new Dictionary<string, string?> { ["placeholder"] = box.GetAttribute("placeholder"), ["name"] = box.GetAttribute("aria-label"), ["clearName"] = root.QuerySelector("button.hl-search-input__clear")!.GetAttribute("aria-label") };
                    if (withDir) result["dir"] = root.GetAttribute("dir");
                    return result;
                }
                Dictionary<string, string?> Expected(System.Text.Json.JsonElement value) => value.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString());
                AngleSharp.Dom.IElement InHost(System.Text.Json.JsonElement catalog, string? placeholder = null, string? clearLabel = null) =>
                    Render<HarborlineLocaleProvider>(p => p.Add(x => x.Locale, catalog.GetProperty("locale").GetString()).Add(x => x.Catalog, catalog.GetProperty("entries").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString()!))
                        .AddChildContent<HarborlineSearchInput>(c => { c.Add(x => x.Value, input.GetProperty("value").GetString()); if (placeholder is not null) c.Add(x => x.Placeholder, placeholder); if (clearLabel is not null) c.Add(x => x.ClearLabel, clearLabel); })).Find(".hl-search-input");
                var value = input.GetProperty("value").GetString();
                Assert.Equal(Expected(expected.GetProperty("defaultCatalog")), Copy(Render<HarborlineSearchInput>(p => p.Add(x => x.Value, value)).Find(".hl-search-input"), false));
                // A catalog change in one live tree: French, then Arabic (rtl), then the en-XA pseudo-locale.
                var catalogs = input.GetProperty("hostCatalogs").EnumerateArray().ToArray();
                Dictionary<string, string> Entries(System.Text.Json.JsonElement catalog) => catalog.GetProperty("entries").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString()!);
                var live = Render<HarborlineLocaleProvider>(p => p.Add(x => x.Locale, catalogs[0].GetProperty("locale").GetString()).Add(x => x.Catalog, Entries(catalogs[0])).AddChildContent<HarborlineSearchInput>(c => c.Add(x => x.Value, input.GetProperty("value").GetString())));
                var seen = new List<Dictionary<string, string?>> { Copy(live.Find(".hl-search-input"), true) };
                foreach (var catalog in catalogs.Skip(1)) { live.Render(p => p.Add(x => x.Locale, catalog.GetProperty("locale").GetString()).Add(x => x.Catalog, Entries(catalog))); seen.Add(Copy(live.Find(".hl-search-input"), true)); }
                Assert.Equal(expected.GetProperty("hostCatalogs").EnumerateArray().Select(Expected), seen);
                Assert.Equal(Expected(expected.GetProperty("keyEchoHost")), Copy(InHost(input.GetProperty("keyEchoHost")), false));
                var labels = input.GetProperty("accessibleLabels");
                foreach (var kind in new[] { "blank", "explicit" })
                    Assert.Equal(expected.GetProperty("accessibleLabels").GetProperty(kind).GetString(), Render<HarborlineSearchInput>(p => p.Add(x => x.Value, input.GetProperty("value").GetString()).Add(x => x.AccessibleLabel, labels.GetProperty(kind).GetString())).Find("input").GetAttribute("aria-label"));
                var blank = input.GetProperty("blankCaller");
                Assert.Equal(Expected(expected.GetProperty("blankCaller")), Copy(InHost(catalogs[1], blank.GetProperty("placeholder").GetString(), blank.GetProperty("clearLabel").GetString()), false));
                Assert.Equal(Expected(expected.GetProperty("blankHost")), Copy(InHost(input.GetProperty("blankHost")), false));
                break;
            }
            case "search-input.unmount":
            {
                var h=Mount();h.Type(input.GetProperty("draft").GetString()!);
                Assert.True(input.GetProperty("unmountBeforeDelay").GetBoolean());
                DisposeComponentsAsync().GetAwaiter().GetResult();
                Wait(600);
                Assert.Equal(Strings(expected.GetProperty("published")),h.Published.ToArray());
                break;
            }
            case "search-input.invalid-delay":
            {
                var error=Assert.Throws<InvalidOperationException>(()=>Render<HarborlineSearchInput>(p=>p.Add(x=>x.DebounceMs,input.GetProperty("debounceMs").GetInt32())));
                Assert.Equal(expected.GetProperty("error").GetString(),error.Message);
                break;
            }
            default:
                Assert.Contains(id,Unasserted);
                break;
        }
    }
    private static void AssertFixturePrefix(string prefix){var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
