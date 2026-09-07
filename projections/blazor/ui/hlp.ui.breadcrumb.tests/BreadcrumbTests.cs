using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class BreadcrumbTests : BunitContext
{
    private static readonly IReadOnlyList<HarborlineBreadcrumbItem> Items =
    [
        new("home", "Home", "/"),
        new("section", "Section", "/section", false),
        new("current", "Current"),
    ];

    // The shared fixture is the authority: read the declared case, drive the render from its
    // `input`, and assert its `expected`. A test that parses the injected document and then renders
    // a hardcoded list is not consuming the fixture -- mutating `expected` leaves it green.
    private static JsonElement Case(string id)
    {
        var injected = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (!string.IsNullOrWhiteSpace(injected))
        {
            var value = JsonDocument.Parse(injected).RootElement;
            if (value.GetProperty("id").GetString() == id) return value;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "catalog", "modules.yaml")))
        {
            directory = directory.Parent;
        }

        if (directory is null) throw new InvalidOperationException("missing-repository-root");
        var path = Path.Combine(directory.FullName, "conformance", "hlp.ui.breadcrumb", "fixtures.yaml");
        foreach (var candidate in JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("cases").EnumerateArray())
        {
            if (candidate.GetProperty("id").GetString() == id) return candidate.Clone();
        }

        throw new InvalidOperationException($"missing-neutral-fixture: {id}");
    }

    // Cases that declare no `items`, or declare them as bare labels, keep the shared list; every
    // case that declares object items -- including the empty array -- drives the render from them.
    private static IReadOnlyList<HarborlineBreadcrumbItem> ItemsOf(JsonElement fixture)
    {
        if (!fixture.GetProperty("input").TryGetProperty("items", out var declared)) return Items;
        var items = new List<HarborlineBreadcrumbItem>();
        var index = 0;
        foreach (var item in declared.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) return Items;
            items.Add(new HarborlineBreadcrumbItem(
                item.TryGetProperty("id", out var id) ? id.GetString()! : $"item-{index}",
                item.GetProperty("label").GetString()!,
                item.TryGetProperty("href", out var href) ? href.GetString() : null,
                item.TryGetProperty("current", out var current) ? current.GetBoolean() : null));
            index++;
        }

        return items;
    }

    [Fact]
    public void OrderedTrailInfersCurrentAndPreservesHostAttributes()
    {
        var cut = Render<HarborlineBreadcrumb>(p => p
            .Add(x => x.Items, Items)
            .Add(x => x.AccessibleLabel, "Location")
            .Add(x => x.Class, "consumer")
            .AddUnmatched("dir", "rtl")
            .AddUnmatched("data-case", "shared"));
        Assert.Equal("Location", cut.Find("nav").GetAttribute("aria-label"));
        Assert.Equal(2, cut.FindAll("a").Count);
        Assert.Equal("page", cut.Find("[aria-current=page]").GetAttribute("aria-current"));
        Assert.Equal(2, cut.FindAll(".hl-breadcrumb__separator").Count);
        Assert.Contains("consumer", cut.Find("nav").ClassList);
        Assert.Equal("rtl", cut.Find("nav").GetAttribute("dir"));
    }

    // Ticket 124: an empty trail is a refusal to render, so there is no landmark, no list and no
    // text -- not an empty <nav> for assistive technology to announce.
    [Fact]
    public void EmptyTrailRendersNothingAtAll()
    {
        var fixture = Case("breadcrumb.empty");
        Assert.False(fixture.GetProperty("expected").GetProperty("rendered").GetBoolean());

        var cut = Render<HarborlineBreadcrumb>(p => p
            .Add(x => x.Items, ItemsOf(fixture))
            .Add(x => x.AccessibleLabel, "Location")
            .Add(x => x.Class, "consumer"));
        Assert.Empty(cut.Markup.Trim());
        Assert.Empty(cut.FindAll("nav"));
        Assert.Empty(cut.FindAll("ol"));
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.breadcrumb")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;

        var fixture = JsonDocument.Parse(raw).RootElement;
        var id = fixture.GetProperty("id").GetString();
        Assert.StartsWith("breadcrumb.", id);

        var expected = fixture.GetProperty("expected");
        var cut = Render<HarborlineBreadcrumb>(p => p.Add(x => x.Items, ItemsOf(fixture)));

        if (expected.TryGetProperty("rendered", out var rendered) && !rendered.GetBoolean())
        {
            Assert.Empty(cut.Markup.Trim());
            return;
        }

        Assert.NotEmpty(cut.FindAll("nav"));
        if (expected.TryGetProperty("separators", out var separators))
        {
            Assert.Equal(separators.GetInt32(), cut.FindAll(".hl-breadcrumb__separator").Count);
        }

        Assert.Equal("page", cut.Find("[aria-current=page]").GetAttribute("aria-current"));

        if (id != "breadcrumb.current-class") return;
        Assert.Equal(Classes(expected, "currentClasses"), cut.Find("[aria-current=page]").ClassList);

        // The parity this case exists to prove needs both kinds of text span in one trail: the
        // current one carries the modifier, every other one carries the base class alone.
        var plain = cut.FindAll(".hl-breadcrumb__text:not([aria-current])");
        Assert.NotEmpty(plain);
        foreach (var span in plain) Assert.Equal(Classes(expected, "textClasses"), span.ClassList);
    }

    private static string[] Classes(System.Text.Json.JsonElement expected, string property) =>
        expected.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();
}
