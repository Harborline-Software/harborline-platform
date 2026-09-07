using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// The Blazor half of conformance/hlp.ui.detail-panel/drilldown-v1.json. Both projections replay
/// ONE fixture: its declared initial state, every declared transition and every declared
/// validation case. Nothing here restates an expectation the fixture does not own.
/// </summary>
public sealed class DetailPanelDrilldownTests : BunitContext
{
    /// <summary>The rail query is irrelevant here: every case sets RailCapable explicitly.</summary>
    private sealed class RailMedia : IMediaQueryObserver
    {
        public ValueTask<IMediaQuerySubscription> ObserveAsync(string query, Func<MediaQueryChange, ValueTask> onChanged, CancellationToken cancellationToken = default)
            => new(new Subscription(query));

        private sealed class Subscription(string query) : IMediaQuerySubscription
        {
            public string Query => query;
            public bool Matches => true;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static string Repo(string relative) => Path.Combine(
        Environment.GetEnvironmentVariable("HARBORLINE_REPO_ROOT") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../")),
        relative);

    private readonly JsonElement fixture = JsonDocument
        .Parse(File.ReadAllText(Repo("conformance/hlp.ui.detail-panel/drilldown-v1.json"))).RootElement.Clone();

    public DetailPanelDrilldownTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IMediaQueryObserver>(new RailMedia());
    }

    private DetailPanelSubject Subject(string name)
    {
        // The fixture is the authority: a name it does not declare is an error, never an empty object.
        if (!fixture.GetProperty("objects").TryGetProperty(name, out var declared))
            throw new InvalidOperationException($"fixture object {name} is not declared");
        return new DetailPanelSubject(
            declared.GetProperty("id").GetString()!,
            declared.GetProperty("title").GetString()!,
            declared.GetProperty("route").GetString()!,
            [.. declared.GetProperty("facets").EnumerateArray()
                .Select(f => new DetailPanelFacet(f.GetProperty("id").GetString()!, f.GetProperty("label").GetString()!, f.GetProperty("count").GetInt32()))],
            [.. declared.GetProperty("relations").EnumerateArray()
                .Select(r => new DetailPanelRelation(r.GetProperty("id").GetString()!, r.GetProperty("label").GetString()!, r.GetProperty("targetId").GetString()!))]);
    }

    private static RenderFragment Body => builder => builder.AddContent(0, "body");

    [Fact]
    public void ReplaysEveryDeclaredTransitionFromTheDeclaredInitialState()
    {
        var events = new List<string>();
        var initial = fixture.GetProperty("initial");
        var subject = Subject(initial.GetProperty("objectId").GetString()!);
        DetailPanelSubject? pushed = initial.GetProperty("pushedObjectId").ValueKind == JsonValueKind.Null
            ? null
            : Subject(initial.GetProperty("pushedObjectId").GetString()!);

        using var cut = Render<HarborlineDetailPanel>(p => Parameters(p, subject, pushed, events));
        var focusCallsBefore = 0;

        foreach (var transition in fixture.GetProperty("transitions").EnumerateArray())
        {
            var action = transition.GetProperty("action");
            var kind = action.GetProperty("kind").GetString();
            focusCallsBefore = FocusCalls();
            switch (kind)
            {
                case "none":
                    break;
                case "activateFacet":
                    cut.Find($"[role='tab'][data-facet-id='{action.GetProperty("facetId").GetString()}']").Click();
                    break;
                case "key":
                    cut.Find("[role='tab'][aria-selected='true']").KeyDown(new KeyboardEventArgs { Key = action.GetProperty("key").GetString()! });
                    break;
                case "activateRelation":
                    cut.Find($"[data-relation-id='{action.GetProperty("relationId").GetString()}']").Click();
                    break;
                case "activateRelationRefused":
                    Assert.Empty(cut.FindAll($"[data-relation-id='{action.GetProperty("relationId").GetString()}']"));
                    break;
                case "activateBack":
                    cut.Find("[data-detail-panel-back]").Click();
                    break;
                case "activatePromote":
                    cut.Find("[data-detail-panel-promote]").Click();
                    break;
                case "host":
                    subject = Subject(action.GetProperty("objectId").GetString()!);
                    pushed = action.GetProperty("pushedObjectId").ValueKind == JsonValueKind.Null
                        ? null
                        : Subject(action.GetProperty("pushedObjectId").GetString()!);
                    cut.Render(p => Parameters(p, subject, pushed, events));
                    break;
                default:
                    throw new InvalidOperationException($"fixture action {kind} is not declared");
            }

            var expected = transition.GetProperty("expect");
            if (expected.TryGetProperty("title", out var title))
                Assert.Equal(title.GetString(), cut.Find("h2.hl-detail-panel__title").TextContent);
            if (expected.TryGetProperty("tabs", out var tabs))
            {
                Assert.Equal(
                    tabs.EnumerateArray().Select(tab => $"{tab.GetProperty("id").GetString()}|{tab.GetProperty("label").GetString()}|{tab.GetProperty("count").GetInt32()}|{tab.GetProperty("selected").GetBoolean().ToString().ToLowerInvariant()}"),
                    cut.FindAll("[role='tab']").Select(tab => string.Join('|',
                        tab.GetAttribute("data-facet-id"),
                        tab.QuerySelector("[data-facet-label]")!.TextContent,
                        tab.QuerySelector("[data-facet-count]")!.TextContent,
                        tab.GetAttribute("aria-selected"))));
            }
            if (expected.TryGetProperty("activeFacetId", out var active))
            {
                var selected = cut.Find("[role='tab'][aria-selected='true']");
                Assert.Equal(active.GetString(), selected.GetAttribute("data-facet-id"));
                Assert.Equal(selected.Id, cut.Find("[role='tabpanel']").GetAttribute("aria-labelledby"));
            }
            if (expected.TryGetProperty("focusedTabId", out var focused))
            {
                Assert.Equal(
                    new[] { focused.GetString() },
                    cut.FindAll("[role='tab'][tabindex='0']").Select(tab => tab.GetAttribute("data-facet-id")));
                Assert.Equal(
                    focusCallsBefore + fixture.GetProperty("focusRoute").GetProperty("invocationsPerHandledKey").GetInt32(),
                    FocusCalls());
            }
            if (expected.TryGetProperty("backRow", out var back))
            {
                var rows = cut.FindAll("[data-detail-panel-back]");
                if (back.ValueKind == JsonValueKind.Null) Assert.Empty(rows);
                else Assert.Equal(back.GetString(), Assert.Single(rows).TextContent);
            }
            if (expected.TryGetProperty("relations", out var relations))
            {
                Assert.Equal(
                    relations.EnumerateArray().Select(id => id.GetString()),
                    cut.FindAll("[data-relation-id]").Select(element => element.GetAttribute("data-relation-id")));
            }
            if (expected.TryGetProperty("promote", out var promote))
                Assert.Equal(promote.GetBoolean() ? 1 : 0, cut.FindAll("[data-detail-panel-promote]").Count);

            // Each transition declares exactly the events its own action emits; the log is drained
            // per step so an event emitted one step late cannot hide inside a running tally.
            Assert.Equal(
                expected.GetProperty("events").EnumerateArray().Select(Describe),
                events.ToArray().AsEnumerable());
            events.Clear();
        }
    }

    private static string Describe(JsonElement declared) => declared.GetProperty("kind").GetString() switch
    {
        "facetChange" => $"facetChange:{declared.GetProperty("facetId").GetString()}",
        "followRelation" => $"followRelation:{declared.GetProperty("relationId").GetString()}:{declared.GetProperty("targetId").GetString()}",
        "openAsPage" => $"openAsPage:{declared.GetProperty("route").GetString()}",
        "back" => "back",
        var other => throw new InvalidOperationException($"fixture event {other} is not declared"),
    };

    private int FocusCalls() => JSInterop.Invocations
        .Count(invocation => invocation.Identifier.EndsWith("focus", StringComparison.Ordinal));

    private void Parameters(ComponentParameterCollectionBuilder<HarborlineDetailPanel> parameters, DetailPanelSubject subject, DetailPanelSubject? pushed, List<string> events) => parameters
        .Add(c => c.Label, "Inspector")
        .Add(c => c.Open, true)
        .Add(c => c.RailCapable, true)
        .Add(c => c.Subject, subject)
        .Add(c => c.Pushed, pushed)
        .Add(c => c.ChildContent, Body)
        .Add(c => c.ActiveFacetIdChanged, EventCallback.Factory.Create<string>(this, id => events.Add($"facetChange:{id}")))
        .Add(c => c.OnFollowRelation, EventCallback.Factory.Create<DetailPanelRelation>(this, r => events.Add($"followRelation:{r.Id}:{r.TargetId}")))
        .Add(c => c.OnBack, EventCallback.Factory.Create(this, () => events.Add("back")))
        .Add(c => c.OnOpenAsPage, EventCallback.Factory.Create<string>(this, route => events.Add($"openAsPage:{route}")));

    [Fact]
    public void RendersTheControlledFacetTheHostAddressesNotTheFirstDeclaredOne()
    {
        var addressing = fixture.GetProperty("controlledAddressing");
        using var cut = Render<HarborlineDetailPanel>(p => p
            .Add(c => c.Label, "Inspector").Add(c => c.Open, true).Add(c => c.RailCapable, true)
            .Add(c => c.Subject, Subject(addressing.GetProperty("objectId").GetString()!))
            .Add(c => c.ActiveFacetId, addressing.GetProperty("activeFacetId").GetString())
            .Add(c => c.ChildContent, Body));

        Assert.Equal(addressing.GetProperty("activeFacetId").GetString(), cut.Find("[role='tab'][aria-selected='true']").GetAttribute("data-facet-id"));
    }

    [Fact]
    public void LeavesTheFixtureDeclaredPassThroughKeysToTheSurroundingShell()
    {
        using var cut = Render<HarborlineDetailPanel>(p => p
            .Add(c => c.Label, "Inspector").Add(c => c.Open, true).Add(c => c.RailCapable, true)
            .Add(c => c.Subject, Subject("PMP-0412")).Add(c => c.ChildContent, Body));
        var before = cut.Find("[role='tab'][aria-selected='true']").GetAttribute("data-facet-id");

        foreach (var key in fixture.GetProperty("passThroughKeys").EnumerateArray())
        {
            cut.Find("[role='tab'][aria-selected='true']").KeyDown(new KeyboardEventArgs { Key = key.GetString()! });
            Assert.Equal(before, cut.Find("[role='tab'][aria-selected='true']").GetAttribute("data-facet-id"));
        }
    }

    [Fact]
    public void DeclaresTheFixtureHandledKeysToTheJavaScriptDefaultRoute()
    {
        using var cut = Render<HarborlineDetailPanel>(p => p
            .Add(c => c.Label, "Inspector").Add(c => c.Open, true).Add(c => c.RailCapable, true)
            .Add(c => c.Subject, Subject("PMP-0412")).Add(c => c.ChildContent, Body));

        // The rendered set IS the set the shell handles: remove a key from the fixture or add one
        // and this goes red until the shell agrees.
        Assert.Equal(
            fixture.GetProperty("handledKeys").EnumerateArray().Select(key => key.GetString()),
            cut.Find("[role='tablist']").GetAttribute("data-handled-keys")!.Split(' '));
        // The route itself: the tab bar is handed to the shipped module, whose per-key behaviour
        // conformance/hlp.ui.detail-panel/detail-panel-tabs.test.mjs replays from the same field.
        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "import"
            && invocation.Arguments.Any(argument => argument is string path && path.EndsWith("detail-panel-tabs.js", StringComparison.Ordinal)));
        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier.EndsWith("connect", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusesTheInvalidDeclarationsTheFixtureNames()
    {
        foreach (var invalid in fixture.GetProperty("validationCases").EnumerateArray())
        {
            var name = invalid.GetProperty("name").GetString();
            var error = Assert.Throws<InvalidOperationException>(() => Render<HarborlineDetailPanel>(p =>
            {
                p.Add(c => c.Label, "Inspector").Add(c => c.Open, true).Add(c => c.RailCapable, true).Add(c => c.ChildContent, Body);
                if (invalid.TryGetProperty("pushedOnly", out _)) p.Add(c => c.Pushed, Subject("WO-8841"));
                else
                {
                    var declared = invalid.GetProperty("object");
                    p.Add(c => c.Subject, new DetailPanelSubject(
                        declared.GetProperty("id").GetString()!,
                        declared.GetProperty("title").GetString()!,
                        declared.GetProperty("route").GetString()!,
                        [.. declared.GetProperty("facets").EnumerateArray()
                            .Select(f => new DetailPanelFacet(f.GetProperty("id").GetString()!, f.GetProperty("label").GetString()!, f.GetProperty("count").GetInt32()))],
                        []));
                    if (invalid.TryGetProperty("activeFacetId", out var facetId)) p.Add(c => c.ActiveFacetId, facetId.GetString());
                }
            }));
            Assert.Equal(invalid.GetProperty("expected").GetString(), error.Message);
        }
    }
}
