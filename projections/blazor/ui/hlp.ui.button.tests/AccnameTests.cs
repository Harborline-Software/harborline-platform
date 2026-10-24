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

    // ---- 2A judged against the shown state: the root and its ancestors count as shown ----

    [Fact]
    public void InsideACollapsedPanelALabelsHiddenTextStillDoesNotCount()
    {
        // HarborlineFormField's required asterisk is aria-hidden; collapsing the panel must not expose it.
        var cut = Markup("""<div hidden><label for="e"><span>Email</span><span aria-hidden="true">*</span></label><input id="e"></div>""");
        Assert.Equal("Email", Accname.AccessibleName(cut.Find("input")));
    }

    [Fact]
    public void InsideACollapsedPanelALabelledByTargetsHiddenDescendantsStillDoNotCount()
    {
        var cut = Markup("""
            <section hidden>
              <div role="dialog" aria-labelledby="t"></div>
              <h2 id="t">Settings<span style="display:none"> draft</span><span hidden> beta</span><span aria-hidden="true"> *</span></h2>
            </section>
            """);
        Assert.Equal("Settings", Accname.AccessibleName(cut.Find("[role=dialog]")));
    }

    [Fact]
    public void InsideACollapsedPanelANameMadeOnlyOfHiddenTextIsEmpty()
    {
        var label = Markup("""<div hidden><label><span aria-hidden="true">Email</span><input id="e"></label></div>""");
        Assert.Contains("has no accessible name", Fails(() => Accname.AccessibleName(label.Find("input"))).Message, StringComparison.Ordinal);
        var dialog = Markup("""<div role="dialog" hidden aria-labelledby="t"><h2 id="t"><span aria-hidden="true">Settings</span></h2></div>""");
        Assert.Contains("has no accessible name", Fails(() => Accname.AccessibleName(dialog.Find("[role=dialog]"))).Message, StringComparison.Ordinal);
    }

    // ---- 2A and 2E rules pinned with text (each fixture changes its result if its rule is dropped) ----

    [Fact]
    public void AriaHiddenTextIsLeftOutOfContent()
    {
        var cut = Markup("""<button><span aria-hidden="true">×</span>Close</button>""");
        Assert.Equal("Close", Accname.AccessibleName(cut.Find("button")));
    }

    [Fact]
    public void VisibilityHiddenOrCollapseAndScriptOrStyleTextIsLeftOutOfContent()
    {
        var cut = Markup("""<button>Save<span style="visibility:hidden"> a</span><span style="visibility: collapse !important"> b</span><script>c</script><style>d</style></button>""");
        Assert.Equal("Save", Accname.AccessibleName(cut.Find("button")));
    }

    [Fact]
    public void ATargetHiddenByAnAncestorOffTheRootsChainKeepsItsHiddenDescendants()
    {
        var cut = Markup("""<div role="dialog" aria-labelledby="h"></div><div hidden><span id="h">Title<span aria-hidden="true"> too</span></span></div>""");
        Assert.Equal("Title too", Accname.AccessibleName(cut.Find("[role=dialog]")));
    }

    [Fact]
    public void AHiddenLabelForStillNamesItsControl()
    {
        var cut = Markup("""<label for="q" hidden>Search</label><input id="q">""");
        Assert.Equal("Search", Accname.AccessibleName(cut.Find("#q")));
    }

    [Fact]
    public void AWrappingLabelNamesOnlyItsFirstLabelableDescendant()
    {
        var cut = Markup("""<label>Name <input id="a"><input id="b" title="Other"></label>""");
        Assert.Equal("Name", Accname.AccessibleName(cut.Find("#a")));
        Assert.Equal("Other", Accname.AccessibleName(cut.Find("#b")));
    }

    // ---- 2C embedded controls ----

    [Fact]
    public void ADirectlyReferencedEmbeddedControlFailsAsNotCovered()
    {
        var name = Markup("""<div role="region" aria-labelledby="q"></div><label for="q">Search</label><input id="q" value="cats">""");
        Assert.Contains("accname 2C embedded-control value not covered", Fails(() => Accname.AccessibleName(name.Find("[role=region]"))).Message, StringComparison.Ordinal);
        var description = Markup("""<button aria-describedby="s">Go</button><select id="s"><option selected>One</option></select>""");
        Assert.Contains("accname 2C embedded-control value not covered", Fails(() => Accname.AccessibleDescription(description.Find("button"))).Message, StringComparison.Ordinal);
    }

    // ---- elements that never contribute ----

    [Fact]
    public void ScriptStyleAndNoscriptNeverContributeEvenUnderAHiddenReferencedNode()
    {
        var cut = Markup("""
            <div role="dialog" aria-labelledby="h" aria-describedby="h"></div>
            <span id="h" hidden>Title<style>.x{color:red}</style><script>var a=1</script><noscript>Enable JS</noscript></span>
            """);
        Assert.Equal("Title", Accname.AccessibleName(cut.Find("[role=dialog]")));
        Assert.Equal("Title", Accname.AccessibleDescription(cut.Find("[role=dialog]")));
    }

    [Fact]
    public void AHiddenInputContributesNothingEvenWhenReferenced()
    {
        var cut = Markup("""<div role="region" aria-labelledby="v t"></div><input type="hidden" id="v" value="secret"><span id="t">Title</span>""");
        Assert.Equal("Title", Accname.AccessibleName(cut.Find("[role=region]")));
    }

    // ---- 2E host-language label: presentational images, SVG title ----

    [Fact]
    public void APresentationalImageContributesNoAlt()
    {
        var cut = Markup("""
            <button id="presentation"><img role="presentation" alt="Close"></button>
            <button id="none"><img role="none" alt="Close"></button>
            <button id="global"><img role="none" alt="Close" aria-describedby="d"></button><span id="d">x</span>
            <button id="focusable"><img role="presentation" alt="Close" tabindex="-1"></button>
            """);
        Assert.Contains("has no accessible name", Fails(() => Accname.AccessibleName(cut.Find("#presentation"))).Message, StringComparison.Ordinal);
        Assert.Contains("has no accessible name", Fails(() => Accname.AccessibleName(cut.Find("#none"))).Message, StringComparison.Ordinal);
        // ARIA 1.2 presentational-role conflict: a global ARIA attribute or focusability keeps the image's semantics.
        Assert.Equal("Close", Accname.AccessibleName(cut.Find("#global")));
        Assert.Equal("Close", Accname.AccessibleName(cut.Find("#focusable")));
    }

    [Fact]
    public void AnSvgIsNamedByItsFirstTitleChildAndItsDescNeverContributes()
    {
        var cut = Markup("""
            <button id="close"><svg><title>Close</title><desc>An X shape</desc></svg></button>
            <button id="save"><svg role="img"><desc>Floppy disk</desc><title>Save</title><title>Ignored</title><metadata>m</metadata></svg> file</button>
            <button id="bare"><svg><desc>Only a description</desc><metadata>m</metadata></svg></button>
            """);
        Assert.Equal("Close", Accname.AccessibleName(cut.Find("#close")));
        Assert.Equal("Save file", Accname.AccessibleName(cut.Find("#save")));
        Assert.Contains("has no accessible name", Fails(() => Accname.AccessibleName(cut.Find("#bare"))).Message, StringComparison.Ordinal);
    }

    // ---- roles: summary, fallback tokens ----

    [Fact]
    public void TheFirstSummaryOfDetailsTakesItsNameFromContent()
    {
        var cut = Markup("""
            <details><summary id="first">More info</summary><summary id="second">Second</summary><p>Body</p></details>
            <summary id="loose">Loose</summary>
            """);
        Assert.Equal("More info", Accname.AccessibleName(cut.Find("#first")));
        Fails(() => Accname.AccessibleName(cut.Find("#second")));
        Fails(() => Accname.AccessibleName(cut.Find("#loose")));
    }

    [Fact]
    public void TheFirstRoleTokenAria12RecognisesWins()
    {
        var cut = Markup("""<div id="fallback" role="foo button">Go</div><div id="unknown" role="foo bar">Go</div><div id="region" role="sectionhead region">Go</div>""");
        Assert.Equal("Go", Accname.AccessibleName(cut.Find("#fallback")));
        Fails(() => Accname.AccessibleName(cut.Find("#unknown")));
        // sectionhead is abstract in ARIA 1.2, so region is the role, and a region does not take its name from content.
        Fails(() => Accname.AccessibleName(cut.Find("#region")));
    }

    // ---- label[for]: the same strictness as the ARIA id references ----

    [Fact]
    public void ALabelForADuplicatedIdFails()
    {
        var cut = Markup("""<label for="f">Email</label><input id="f" class="one"><label for="f">Phone</label><input id="f" class="two">""");
        Assert.Contains("for references id \"f\", which 2 elements carry", Fails(() => Accname.AccessibleName(cut.Find(".two"))).Message, StringComparison.Ordinal);
        Assert.Contains("for references id \"f\", which 2 elements carry", Fails(() => Accname.AssertIdReferencesResolve(cut)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOrphanOrMisdirectedLabelForFailsTheWiringCheck()
    {
        var cut = Markup("""<label for="gone">Orphan</label><input id="g" title="Fallback"><span id="s">x</span><label for="s">Span</label><label for="">Empty</label>""");
        Assert.Equal("Fallback", Accname.AccessibleName(cut.Find("#g")));
        var wiring = Fails(() => Accname.AssertIdReferencesResolve(cut)).Message;
        Assert.Contains("for references id \"gone\"", wiring, StringComparison.Ordinal);
        Assert.Contains("for references id \"s\", which is on a <span>, not a labelable element", wiring, StringComparison.Ordinal);
        Assert.Contains("for is present but names no id", wiring, StringComparison.Ordinal);
    }

    [Fact]
    public void SoundLabelWiringPasses()
    {
        var cut = Markup("""<label for="e">Email</label><input id="e"><label>Name <input id="n"></label>""");
        Accname.AssertIdReferencesResolve(cut);
        Assert.Equal("Email", Accname.AccessibleName(cut.Find("#e")));
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
        Assert.Contains("which no element in this render carries", named.Message, StringComparison.Ordinal);
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
    public void AssertReferencesTellsAnIdenticalElementFromAnotherRenderApart()
    {
        // The same markup rendered twice: only an identity comparison can tell b's #x from a's.
        const string html = """<div id="owner" aria-controls="x"></div><p id="x"></p>""";
        var a = Markup(html);
        var b = Markup(html);
        Accname.AssertReferences(a.Find("#owner"), "aria-controls", a.Find("#x"));
        var other = Fails(() => Accname.AssertReferences(a.Find("#owner"), "aria-controls", b.Find("#x"))).Message;
        Assert.Contains("expected[0] <p id=\"x\"> belongs to a different render than <div id=\"owner\" aria-controls=\"x\">", other, StringComparison.Ordinal);
        Assert.Contains("re-query it from the same rendered component", other, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIdOutsideAFindComponentRenderIsReportedAsOutsideThatRender()
    {
        var cut = Render(builder =>
        {
            builder.AddMarkupContent(0, """<button id="h">Open</button>""");
            builder.OpenComponent<DescribedByParentId>(1);
            builder.CloseComponent();
        });
        Assert.Equal("Open", Accname.AccessibleDescription(cut.Find("#c")));
        var child = Fails(() => Accname.AccessibleDescription(cut.FindComponent<DescribedByParentId>().Find("#c"))).Message;
        Assert.Contains("aria-describedby references id \"h\", which no element in this render carries", child, StringComparison.Ordinal);
        Assert.Contains("a FindComponent<T>() render excludes its parent's markup", child, StringComparison.Ordinal);
    }

    private sealed class DescribedByParentId : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) =>
            builder.AddMarkupContent(0, """<span id="c" aria-describedby="h">x</span>""");
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
