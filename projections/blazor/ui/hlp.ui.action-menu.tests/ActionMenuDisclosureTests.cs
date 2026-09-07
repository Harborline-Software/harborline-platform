using System.Text.Json;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Components;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// The Blazor half of conformance/hlp.ui.action-menu/disclosure-v1.json. Both projections replay
/// ONE fixture: its declared initial state, every declared transition and every declared fence.
/// Nothing here restates an expectation the fixture does not own. The computed style of the hint
/// token is asserted by the Blazor lane's JavaScript test, which bUnit cannot run
/// (conformance/hlp.ui.action-menu/action-menu-disclosure.test.mjs).
/// </summary>
public sealed class ActionMenuDisclosureTests : BunitContext
{
    private static string Repo(string relative) => Path.Combine(
        Environment.GetEnvironmentVariable("HARBORLINE_REPO_ROOT") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../")),
        relative);

    private readonly JsonElement fixture = JsonDocument
        .Parse(File.ReadAllText(Repo("conformance/hlp.ui.action-menu/disclosure-v1.json"))).RootElement.Clone();

    public ActionMenuDisclosureTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IOutsidePointerObserver>(new NoOutsidePointer());
    }

    /// <summary>The fixture is the authority: a name it does not declare is an error, never a skip.</summary>
    private JsonElement Declared(string itemId)
    {
        foreach (var candidate in fixture.GetProperty("items").EnumerateArray())
            if (candidate.GetProperty("id").GetString() == itemId) return candidate;
        throw new InvalidOperationException($"missing-fixture-item: {itemId}");
    }

    private static string? Text(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) ? value.GetString() : null;

    private IReadOnlyList<ActionMenuEntry> Entries(IReadOnlyDictionary<string, bool> checkedState)
        => [.. fixture.GetProperty("items").EnumerateArray().Select(raw =>
        {
            var id = raw.GetProperty("id").GetString()!;
            return (ActionMenuEntry)new ActionMenuItem(
                id,
                raw.GetProperty("label").GetString()!,
                Destructive: raw.TryGetProperty("destructive", out var destructive) && destructive.GetBoolean(),
                ShortcutHint: Text(raw, "shortcutHint"),
                KeyShortcuts: Text(raw, "keyShortcuts"),
                Checked: raw.TryGetProperty("checked", out _) ? checkedState[id] : null);
        })];

    private static Dictionary<string, bool> CheckedState(JsonElement declared)
        => declared.EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetBoolean());

    private IRenderedComponent<HarborlineActionMenu> Open(
        IReadOnlyDictionary<string, bool> checkedState,
        Action<ActionMenuItem>? selected = null)
    {
        var cut = Render<HarborlineActionMenu>(p => p
            .Add(x => x.Items, Entries(checkedState))
            .Add(x => x.OnSelect, item => selected?.Invoke(item)));
        cut.Find("button").Click();
        return cut;
    }

    private static IElement Item(IRenderedComponent<HarborlineActionMenu> cut, string itemId)
        => cut.FindAll($"[data-item-id='{itemId}']").SingleOrDefault() as IElement
           ?? throw new InvalidOperationException($"missing-rendered-item: {itemId}");

    [Fact]
    public void EveryDeclaredHintRendersInTheSharedToken()
    {
        var render = fixture.GetProperty("render");
        var hintClass = render.GetProperty("hintClass").GetString()!;
        var cut = Open(CheckedState(fixture.GetProperty("initial").GetProperty("checked")));

        foreach (var declared in render.GetProperty("hints").EnumerateObject())
        {
            var element = Item(cut, declared.Name);
            var hint = element.QuerySelector($".{hintClass}");
            Assert.NotNull(hint);
            Assert.Equal(declared.Value.GetString(), hint!.TextContent);
            // The hint is the LAST child: the alignment is the token's, never a call site's.
            Assert.Same(hint, element.LastElementChild);
            Assert.Equal(declared.Value.GetString(), Text(Declared(declared.Name), "shortcutHint"));
        }
        foreach (var without in render.GetProperty("withoutHint").EnumerateArray())
            Assert.Null(Item(cut, without.GetString()!).QuerySelector($".{hintClass}"));
    }

    /// <summary>The accessible name is the text AT reads: an aria-hidden subtree contributes nothing.</summary>
    private static string AccessibleName(IElement element)
        => string.Concat(element.ChildNodes
            .Where(node => node is not IElement child || child.GetAttribute("aria-hidden") != "true")
            .Select(node => node.TextContent)).Trim();

    [Fact]
    public void EveryDeclaredChordReachesAssistiveTechnologyThroughAriaKeyshortcuts()
    {
        var render = fixture.GetProperty("render");
        var hintClass = render.GetProperty("hintClass").GetString()!;
        var decorative = render.GetProperty("hintIsDecorative").GetBoolean();
        var chords = render.GetProperty("keyShortcuts");
        var cut = Open(CheckedState(fixture.GetProperty("initial").GetProperty("checked")));

        foreach (var declared in chords.EnumerateObject())
        {
            var element = Item(cut, declared.Name);
            Assert.Equal(declared.Value.GetString(), element.GetAttribute("aria-keyshortcuts"));
            Assert.Equal(declared.Value.GetString(), Text(Declared(declared.Name), "keyShortcuts"));
            // The hint stays visual: the chord reaches AT through the attribute, not the name.
            Assert.Equal(decorative ? "true" : null, element.QuerySelector($".{hintClass}")!.GetAttribute("aria-hidden"));
        }
        foreach (var declared in render.GetProperty("accessibleNames").EnumerateObject())
        {
            var element = Item(cut, declared.Name);
            Assert.Equal(declared.Value.GetString(), AccessibleName(element));
            if (!chords.TryGetProperty(declared.Name, out _))
                Assert.Null(element.GetAttribute("aria-keyshortcuts"));
        }
    }

    [Fact]
    public void TogglesAreCheckboxItemsWithACheckMarkAndCommandsAreNeither()
    {
        var render = fixture.GetProperty("render");
        var checkClass = render.GetProperty("checkClass").GetString()!;
        var cut = Open(CheckedState(fixture.GetProperty("initial").GetProperty("checked")));

        foreach (var declared in render.GetProperty("checkboxItems").EnumerateObject())
        {
            var element = Item(cut, declared.Name);
            Assert.Equal("menuitemcheckbox", element.GetAttribute("role"));
            Assert.Equal(declared.Value.GetString(), element.GetAttribute("aria-checked"));
            var mark = element.QuerySelector($".{checkClass}");
            Assert.NotNull(mark);
            Assert.Equal(declared.Value.GetString() == "true", mark!.TextContent == "✓");
        }
        foreach (var command in render.GetProperty("commandItems").EnumerateArray())
        {
            var element = Item(cut, command.GetString()!);
            Assert.Equal("menuitem", element.GetAttribute("role"));
            Assert.Null(element.GetAttribute("aria-checked"));
            Assert.Null(element.QuerySelector($".{checkClass}"));
        }
    }

    [Fact]
    public void ReplaysEveryDeclaredTransitionFromTheDeclaredInitialState()
    {
        var checkedState = CheckedState(fixture.GetProperty("initial").GetProperty("checked"));
        var invoked = new List<string>();
        Assert.Equal(
            fixture.GetProperty("initial").GetProperty("invoked").EnumerateArray().Select(x => x.GetString()!).ToList(),
            invoked);

        // The host owns the value and changes it through the ONE callback every entry reports through.
        var cut = Render<HarborlineActionMenu>(p => p
            .Add(x => x.Items, Entries(checkedState))
            .Add(x => x.OnSelect, item =>
            {
                invoked.Add(item.Id);
                if (item.Checked is not null) checkedState[item.Id] = !checkedState[item.Id];
            }));

        foreach (var transition in fixture.GetProperty("transitions").EnumerateArray())
        {
            var action = transition.GetProperty("action");
            switch (action.GetProperty("kind").GetString())
            {
                case "activate":
                    cut.Find("button").Click();
                    Item(cut, Declared(action.GetProperty("itemId").GetString()!).GetProperty("id").GetString()!).Click();
                    break;
                case "host":
                    // Value, not identity: the host hands back an equal-by-value map, and a
                    // re-materialised entry list carries the marks it declares.
                    foreach (var declared in CheckedState(action.GetProperty("checked"))) checkedState[declared.Key] = declared.Value;
                    break;
                default:
                    throw new InvalidOperationException($"unknown-fixture-action: {action.GetProperty("kind").GetString()}");
            }
            cut.Render(p => p.Add(x => x.Items, Entries(checkedState)));

            var expected = CheckedState(transition.GetProperty("expect").GetProperty("checked"));
            Assert.Equal(expected, checkedState);
            Assert.Equal(
                transition.GetProperty("expect").GetProperty("invoked").EnumerateArray().Select(x => x.GetString()!).ToList(),
                invoked);

            cut.Find("button").Click();
            foreach (var declared in expected)
                Assert.Equal(declared.Value ? "true" : "false", Item(cut, declared.Key).GetAttribute("aria-checked"));
            cut.Find("button").Click();
        }
    }

    [Fact]
    public async Task RefusesASecondHomeAtOneScopeAndGivesADifferentScopeItsOwn()
    {
        var scope = fixture.GetProperty("oneHomePerScope");
        var scopeId = scope.GetProperty("scopeId").GetString()!;
        var items = Entries(CheckedState(fixture.GetProperty("initial").GetProperty("checked")));

        var first = Render<HarborlineActionMenu>(p => p.Add(x => x.Items, items).Add(x => x.ScopeId, scopeId));
        var refusal = Assert.Throws<InvalidOperationException>(
            () => Render<HarborlineActionMenu>(p => p.Add(x => x.Items, items).Add(x => x.ScopeId, scopeId)));
        Assert.Equal(scope.GetProperty("error").GetString(), refusal.Message);

        // A different scope is a different home, and both mount in the same shell instance.
        var other = Render<HarborlineActionMenu>(p => p.Add(x => x.Items, items)
            .Add(x => x.ScopeId, scope.GetProperty("otherScopeId").GetString()!));
        Assert.NotNull(other.Instance);

        // Unmounting releases the home, so the same scope can take a home again.
        await first.Instance.DisposeAsync();
        Render<HarborlineActionMenu>(p => p.Add(x => x.Items, items).Add(x => x.ScopeId, scopeId));
    }

    [Fact]
    public void DerivesAScopeWhenTheComposerDeclaresNoneSoNoMenuCanDeclineAHome()
    {
        if (!fixture.TryGetProperty("derivedScope", out var derived)) throw new InvalidOperationException("missing-fixture-case: derivedScope");
        var rootScopeId = derived.GetProperty("rootScopeId").GetString()!;
        var hostScopeId = derived.GetProperty("hostScopeId").GetString()!;
        var error = derived.GetProperty("error").GetString()!;
        var items = Entries(CheckedState(fixture.GetProperty("initial").GetProperty("checked")));
        Assert.Equal(ActionMenuScopes.RootScopeId, rootScopeId);

        // Omission does not opt out: the chain terminates at the shell root, so the second
        // undeclared menu in this shell instance is the same scope and is refused.
        Render<HarborlineActionMenu>(p => p.Add(x => x.Items, items));
        Assert.Equal(error, Assert.Throws<InvalidOperationException>(
            () => Render<HarborlineActionMenu>(p => p.Add(x => x.Items, items))).Message);

        // The declared root scope names the SAME home the undeclared menu resolved.
        Assert.Equal(error, Assert.Throws<InvalidOperationException>(
            () => Render<HarborlineActionMenu>(p => p.Add(x => x.Items, items).Add(x => x.ScopeId, rootScopeId))).Message);

        // The nearest scope host supplies the scope: a menu under it collides with that host id,
        // and a menu that declares its own scope overrides the host and keeps its own home.
        RenderInScope(hostScopeId, items, null);
        Assert.Equal(error, Assert.Throws<InvalidOperationException>(
            () => Render<HarborlineActionMenu>(p => p.Add(x => x.Items, items).Add(x => x.ScopeId, hostScopeId))).Message);
        RenderInScope(hostScopeId, items, fixture.GetProperty("oneHomePerScope").GetProperty("scopeId").GetString()!);
    }

    private void RenderInScope(string hostScopeId, IReadOnlyList<ActionMenuEntry> items, string? scopeId) =>
        Render<CascadingValue<string>>(p => p
            .Add(x => x.Name, ActionMenuScopes.CascadingName)
            .Add(x => x.Value, hostScopeId)
            .Add(x => x.IsFixed, true)
            .AddChildContent<HarborlineActionMenu>(c =>
            {
                c.Add(x => x.Items, items);
                if (scopeId is not null) c.Add(x => x.ScopeId, scopeId);
            }));

    [Fact]
    public void RefusesAFacetAsAnEntryUsingTheFacetsTheDetailPanelDeclares()
    {
        var fence = fixture.GetProperty("noFacetsInTheMenu");
        using var panel = JsonDocument.Parse(File.ReadAllText(Repo(fence.GetProperty("facetSource").GetString()!)));
        if (!panel.RootElement.GetProperty("objects").TryGetProperty(fence.GetProperty("facetObjectId").GetString()!, out var subject))
            throw new InvalidOperationException("missing-fixture-object");
        var facetIds = subject.GetProperty("facets").EnumerateArray().Select(f => f.GetProperty("id").GetString()!).ToList();
        var itemId = fence.GetProperty("itemId").GetString()!;
        Assert.Contains(itemId, facetIds);

        IReadOnlyList<ActionMenuEntry> withFacet = [new ActionMenuItem(itemId, "Attachments")];
        var refusal = Assert.Throws<InvalidOperationException>(
            () => Render<HarborlineActionMenu>(p => p.Add(x => x.Items, withFacet).Add(x => x.FacetIds, facetIds)));
        Assert.Equal(fence.GetProperty("error").GetString(), refusal.Message);

        // Without declared facets the composer has stated nothing to fence against. Each menu here
        // declares its own scope: both live in one shell instance, and every menu has a home.
        Render<HarborlineActionMenu>(p => p.Add(x => x.Items, withFacet).Add(x => x.ScopeId, $"{itemId}:no-facets-declared"));
        Render<HarborlineActionMenu>(p => p
            .Add(x => x.Items, Entries(CheckedState(fixture.GetProperty("initial").GetProperty("checked"))))
            .Add(x => x.FacetIds, facetIds)
            .Add(x => x.ScopeId, $"{itemId}:no-facet-entries"));
    }

    private sealed class NoOutsidePointer : IOutsidePointerObserver
    {
        public ValueTask<IOutsidePointerRegistration> ObserveAsync(Microsoft.AspNetCore.Components.ElementReference element, Func<OutsidePointerEvent, ValueTask> callback, OutsidePointerOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IOutsidePointerRegistration> ObserveAsync(IReadOnlyList<Microsoft.AspNetCore.Components.ElementReference> elements, Func<OutsidePointerEvent, ValueTask> callback, OutsidePointerOptions? options = null, CancellationToken cancellationToken = default)
            => new(new Registration());

        private sealed class Registration : IOutsidePointerRegistration
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public ValueTask SetEnabledAsync(bool enabled) => ValueTask.CompletedTask;
            public void SetCallback(Func<OutsidePointerEvent, ValueTask> callback) { }
        }
    }
}
