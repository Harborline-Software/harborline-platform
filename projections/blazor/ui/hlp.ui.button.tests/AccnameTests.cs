using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
using Xunit;
using Xunit.Sdk;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// The shared accessible-name and id-wiring helper (<see cref="Accname"/>, control T-944), proved over small
/// markup fixtures rendered through bUnit, so each fixture is parsed exactly as a component's output is.
/// </summary>
public sealed class AccnameTests : BunitContext
{
    private IRenderedComponent<ContainerFragment> Markup(string html) => Render(builder => builder.AddMarkupContent(0, html));

    private static XunitException Fails(Action action) => Assert.Throws<XunitException>(action);

    // ---- accname 1.2 step 2B: aria-labelledby ----

    [Fact]
    public void LabelledByWinsOverAriaLabelAndContent()
    {
        var cut = Markup("""<button aria-labelledby="t" aria-label="Ignored">Content</button><span id="t">Title</span>""");
        Assert.Equal("Title", Accname.AccessibleName(cut.Find("button")));
    }

    [Fact]
    public void MultipleLabelledByIdsAreJoinedInAttributeOrder()
    {
        var cut = Markup("""<div role="region" aria-labelledby="b a"></div><span id="a">Alpha</span><span id="b">Beta</span>""");
        Assert.Equal("Beta Alpha", Accname.AccessibleName(cut.Find("[role=region]")));
    }

    [Fact]
    public void AReferencedNodesOwnLabelledByIsNotFollowed()
    {
        var cut = Markup("""<div role="region" aria-labelledby="a"></div><span id="a" aria-labelledby="b">Alpha</span><span id="b">Beta</span>""");
        Assert.Equal("Alpha", Accname.AccessibleName(cut.Find("[role=region]")));
    }

    [Fact]
    public void LabelledByThatYieldsNoTextFallsThroughToAriaLabel()
    {
        // 2B returns only a non-empty result; the name then comes from 2D. The wiring check still refuses it.
        var cut = Markup("""<section role="dialog" aria-labelledby="t" aria-label="Fallback"></section><div id="t"></div>""");
        Assert.Equal("Fallback", Accname.AccessibleName(cut.Find("section")));
        var failure = Fails(() => Accname.AssertIdReferencesResolve(cut));
        Assert.Contains("aria-labelledby resolves to an empty name", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AHiddenNodeReferencedDirectlyStillNamesAndKeepsItsHiddenDescendants()
    {
        var cut = Markup("""<div role="dialog" aria-labelledby="h"></div><span id="h" hidden>Hidden title <span aria-hidden="true">too</span></span>""");
        Assert.Equal("Hidden title too", Accname.AccessibleName(cut.Find("[role=dialog]")));
    }

    [Fact]
    public void AVisibleReferencedNodeDropsItsHiddenDescendants()
    {
        var cut = Markup("""<div role="dialog" aria-labelledby="h"></div><span id="h">Shown<span hidden> secret</span><span style="display: none"> styled</span></span>""");
        Assert.Equal("Shown", Accname.AccessibleName(cut.Find("[role=dialog]")));
    }

    [Fact]
    public void TheElementAskedAboutIsNamedEvenWhileItIsHidden()
    {
        // A collapsed accordion panel is hidden; its name is still the header it is wired to.
        var cut = Markup("""<div role="region" hidden aria-labelledby="h"></div><button id="h">Alpha</button>""");
        Assert.Equal("Alpha", Accname.AccessibleName(cut.Find("[role=region]")));
    }

    // ---- 2D aria-label, 2E host-language label, 2F/2H content, 2I tooltip ----

    [Fact]
    public void AriaLabelNamesWhenThereIsNoLabelledBy()
    {
        var cut = Markup("""<div role="dialog" aria-label="Inspector">Body</div>""");
        Assert.Equal("Inspector", Accname.AccessibleName(cut.Find("[role=dialog]")));
    }

    [Fact]
    public void BlankAriaLabelIsIgnored()
    {
        var cut = Markup("""<button aria-label="   ">Save</button>""");
        Assert.Equal("Save", Accname.AccessibleName(cut.Find("button")));
    }

    [Fact]
    public void NativeLabelsNameTheirControl()
    {
        var cut = Markup("""
            <label for="email">Email</label><input id="email">
            <label>Name <input id="name"></label>
            <label for="both">First</label><label>Second <input id="both"></label>
            """);
        Assert.Equal("Email", Accname.AccessibleName(cut.Find("#email")));
        Assert.Equal("Name", Accname.AccessibleName(cut.Find("#name")));
        Assert.Equal("First Second", Accname.AccessibleName(cut.Find("#both")));
    }

    [Fact]
    public void AriaLabelBeatsANativeLabel()
    {
        var cut = Markup("""<label for="q">Visible</label><input id="q" aria-label="Search">""");
        Assert.Equal("Search", Accname.AccessibleName(cut.Find("input")));
    }

    [Fact]
    public void ButtonsTakeTheirNameFromContentWithoutHiddenSubtrees()
    {
        var cut = Markup("""
            <button id="a"><span>Save</span><svg aria-hidden="true"><path d="M0 0"/></svg><span hidden> draft</span></button>
            <button id="b"><span>Sa</span><span>ve</span></button>
            <button id="c"><div>Save</div><div>changes</div></button>
            <button id="d">  Save
                 now  </button>
            """);
        Assert.Equal("Save", Accname.AccessibleName(cut.Find("#a")));
        Assert.Equal("Save", Accname.AccessibleName(cut.Find("#b")));
        Assert.Equal("Save changes", Accname.AccessibleName(cut.Find("#c")));
        Assert.Equal("Save now", Accname.AccessibleName(cut.Find("#d")));
    }

    [Fact]
    public void DescendantAriaLabelAndImageAltContributeToContent()
    {
        var cut = Markup("""<a id="home" href="/"><img alt="Home"> page</a><button id="close"><span aria-label="Close"></span></button>""");
        Assert.Equal("Home page", Accname.AccessibleName(cut.Find("#home")));
        Assert.Equal("Close", Accname.AccessibleName(cut.Find("#close")));
    }

    [Fact]
    public void ContentNamesOnlyRolesThatTakeTheirNameFromContent()
    {
        var cut = Markup("""<div role="region">Body text</div><div role="tab">Overview</div><h3>Heading</h3>""");
        Assert.Equal("Overview", Accname.AccessibleName(cut.Find("[role=tab]")));
        Assert.Equal("Heading", Accname.AccessibleName(cut.Find("h3")));
        var failure = Fails(() => Accname.AccessibleName(cut.Find("[role=region]")));
        Assert.Contains("has no accessible name", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TitleIsTheLastResort()
    {
        var cut = Markup("""<button id="tip" title="Tip"></button><button id="text" title="Tip">Text</button>""");
        Assert.Equal("Tip", Accname.AccessibleName(cut.Find("#tip")));
        Assert.Equal("Text", Accname.AccessibleName(cut.Find("#text")));
    }

    // ---- failures: empty name, broken and duplicate ids ----

    [Fact]
    public void AnEmptyNameFailsAndNamesTheElement()
    {
        var cut = Markup("""<button class="hl-icon"><svg aria-hidden="true"></svg></button>""");
        var failure = Fails(() => Accname.AccessibleName(cut.Find("button")));
        Assert.Contains("has no accessible name", failure.Message, StringComparison.Ordinal);
        Assert.Contains("<button class=\"hl-icon\">", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABrokenLabelledByIdFails()
    {
        var cut = Markup("""<div role="region" aria-labelledby="header-0"></div><button id="header-1">Alpha</button>""");
        var named = Fails(() => Accname.AccessibleName(cut.Find("[role=region]")));
        Assert.Contains("aria-labelledby references id \"header-0\"", named.Message, StringComparison.Ordinal);
        Assert.Contains("not in the rendered markup", named.Message, StringComparison.Ordinal);
        var wiring = Fails(() => Accname.AssertIdReferencesResolve(cut));
        Assert.Contains("aria-labelledby references id \"header-0\"", wiring.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneBrokenIdAmongSeveralFails()
    {
        var cut = Markup("""<div role="region" aria-labelledby="a gone"></div><span id="a">Alpha</span>""");
        var failure = Fails(() => Accname.AccessibleName(cut.Find("[role=region]")));
        Assert.Contains("\"gone\"", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateTargetIdFails()
    {
        var cut = Markup("""<button aria-controls="p">Toggle</button><div id="p"></div><div id="p"></div>""");
        var wiring = Fails(() => Accname.AssertIdReferencesResolve(cut));
        Assert.Contains("aria-controls references id \"p\", which 2 elements carry", wiring.Message, StringComparison.Ordinal);
        Fails(() => Accname.AssertReferences(cut.Find("button"), "aria-controls", cut.Find("div")));
    }

    [Fact]
    public void ADuplicateLabelIdFailsTheName()
    {
        var cut = Markup("""<div role="region" aria-labelledby="h"></div><span id="h">One</span><span id="h">Two</span>""");
        var failure = Fails(() => Accname.AccessibleName(cut.Find("[role=region]")));
        Assert.Contains("which 2 elements carry", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReferenceAttributeThatNamesNoIdFails()
    {
        // What a Blazor component renders when the id it interpolates collapses to "": both ends become empty.
        var cut = Markup("""<button aria-controls="" aria-labelledby=" ">Toggle</button><div id="" role="region"></div>""");
        var wiring = Fails(() => Accname.AssertIdReferencesResolve(cut));
        Assert.Contains("aria-controls is present but names no id", wiring.Message, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby is present but names no id", wiring.Message, StringComparison.Ordinal);
        Fails(() => Accname.AccessibleName(cut.Find("button")));
    }

    [Fact]
    public void WiringFailuresAreAllReportedTogether()
    {
        var cut = Markup("""<button aria-controls="x">A</button><button aria-describedby="y">B</button>""");
        var failure = Fails(() => Accname.AssertIdReferencesResolve(cut));
        Assert.Contains("aria-controls references id \"x\"", failure.Message, StringComparison.Ordinal);
        Assert.Contains("aria-describedby references id \"y\"", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SoundWiringPasses()
    {
        var cut = Markup("""
            <button id="h" aria-controls="p" aria-describedby="d">Alpha</button>
            <div id="p" role="region" aria-labelledby="h" hidden>Panel</div>
            <span id="d">Details</span>
            """);
        Accname.AssertIdReferencesResolve(cut);
    }

    // ---- aria-describedby and the description computation ----

    [Fact]
    public void DescribedByJoinsTargetsInAttributeOrder()
    {
        var cut = Markup("""<button aria-describedby="s2 s1">Go</button><span id="s1">one</span><span id="s2">two</span>""");
        Assert.Equal("two one", Accname.AccessibleDescription(cut.Find("button")));
    }

    [Fact]
    public void DescriptionIsEmptyWithoutDescribedByAndFallsBackToAriaDescription()
    {
        var cut = Markup("""<button id="none">Go</button><button id="aria" aria-description="Hint">Go</button><button id="both" aria-describedby="d" aria-description="Hint">Go</button><span id="d">Details</span>""");
        Assert.Equal("", Accname.AccessibleDescription(cut.Find("#none")));
        Assert.Equal("Hint", Accname.AccessibleDescription(cut.Find("#aria")));
        Assert.Equal("Details", Accname.AccessibleDescription(cut.Find("#both")));
    }

    [Fact]
    public void ABrokenDescribedByIdFails()
    {
        var cut = Markup("""<button aria-describedby="subtitle">Go</button>""");
        var failure = Fails(() => Accname.AccessibleDescription(cut.Find("button")));
        Assert.Contains("aria-describedby references id \"subtitle\"", failure.Message, StringComparison.Ordinal);
    }

    // ---- AssertReferences ----

    [Fact]
    public void AssertReferencesComparesTargetsByIdentityInAttributeOrder()
    {
        var cut = Markup("""<div id="owner" aria-controls="b a"></div><p id="a"></p><p id="b"></p>""");
        Accname.AssertReferences(cut.Find("#owner"), "aria-controls", cut.Find("#b"), cut.Find("#a"));
        var swapped = Fails(() => Accname.AssertReferences(cut.Find("#owner"), "aria-controls", cut.Find("#a"), cut.Find("#b")));
        Assert.Contains("aria-controls resolves to [<p id=\"b\">, <p id=\"a\">], expected [<p id=\"a\">, <p id=\"b\">]", swapped.Message, StringComparison.Ordinal);
        Fails(() => Accname.AssertReferences(cut.Find("#owner"), "aria-controls", cut.Find("#b")));
    }

    [Fact]
    public void AssertReferencesTellsTwoStructurallyIdenticalElementsApart()
    {
        // Identity, not structural equality: the second <p> looks exactly like the first.
        var cut = Markup("""<div id="owner" aria-controls="x"></div><section><p id="x"></p></section><p class="twin"></p>""");
        var twin = Fails(() => Accname.AssertReferences(cut.Find("#owner"), "aria-controls", cut.Find(".twin")));
        Assert.Contains("expected [<p class=\"twin\">]", twin.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssertReferencesFailsWhenTheAttributeIsAbsent()
    {
        var cut = Markup("""<button>Toggle</button><div></div>""");
        var failure = Fails(() => Accname.AssertReferences(cut.Find("button"), "aria-controls", cut.Find("div")));
        Assert.Contains("has no aria-controls", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IdsAreMatchedExactlyAndCaseSensitively()
    {
        var cut = Markup("""<div role="region" aria-labelledby="Head"></div><span id="head">lower</span>""");
        var failure = Fails(() => Accname.AccessibleName(cut.Find("[role=region]")));
        Assert.Contains("\"Head\"", failure.Message, StringComparison.Ordinal);
    }
}
