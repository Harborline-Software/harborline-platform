using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ToasterTests : BunitContext
{
    private readonly ToastService service=new();
    public ToasterTests()=>Services.AddSingleton<IToastService>(service);
    [Fact] public void QueueShowsNewestAndUsesRoles(){var cut=Render<HarborlineToaster>(p=>p.Add(x=>x.MaximumVisible,2));service.Show("A");service.Success("B");service.Error("C");cut.WaitForAssertion(()=>Assert.Equal(2,cut.FindAll(".hl-toaster__toast").Count));Assert.DoesNotContain("A",cut.Markup);Assert.Equal("alert",cut.Find("[data-variant=error]").GetAttribute("role"));Assert.False(cut.Find("[role=alert]").HasAttribute("aria-live"));Assert.Equal(2,cut.FindAll(".hl-toaster__marker svg").Count);Assert.DoesNotContain("✓",cut.Find(".hl-toaster__marker").TextContent);Assert.NotNull(cut.Find(".hl-toaster__close svg"));Assert.DoesNotContain("×",cut.Find(".hl-toaster__close").TextContent);}
    [Fact] public void ScopedServicesDoNotShareEntries(){var other=new ToastService();service.Show("Mine");Assert.Single(service.Entries);Assert.Empty(other.Entries);other.Dispose();}
    [Fact] public async Task TrackAsyncRetainsIdentity(){Render<HarborlineToaster>();var result=await service.TrackAsync(Task.FromResult(3),"Loading",value=>$"Done {value}",error=>error.Message);Assert.Equal(3,result);Assert.Single(service.Entries);Assert.Equal(ToastVariant.Success,service.Entries[0].Variant);}
    // T-1107: the region name React matches. Default "Notifications"; AccessibleLabel replaces it, and a
    // caller's own splatted aria-label attribute wins over both.
    [Fact] public void RegionNameDefaultsAndFollowsTheCaller(){var cut=Render<HarborlineToaster>();Assert.Equal("Notifications",cut.Find("section.hl-toaster").GetAttribute("aria-label"));cut.Render(p=>p.Add(x=>x.AccessibleLabel,"الإشعارات"));Assert.Equal("الإشعارات",cut.Find("section.hl-toaster").GetAttribute("aria-label"));cut.Render(p=>p.Add(x=>x.AccessibleLabel,"الإشعارات").AddUnmatched("aria-label","Workspace alerts"));Assert.Equal("Workspace alerts",cut.Find("section.hl-toaster").GetAttribute("aria-label"));}
    // T-1107 review 2: a blank region name is absent, as in React. The interface defines
    // toast-message-required only for an empty toast message, so a blank, whitespace-only or null
    // AccessibleLabel keeps "Notifications" instead of throwing, and a section never renders with a
    // blank aria-label, which would leave it without a name and so not a landmark (HTML-AAM).
    public static TheoryData<string?> BlankNames => new() { "", " \t\n ", null };
    [Theory, MemberData(nameof(BlankNames))] public void BlankAccessibleLabelKeepsTheDefaultName(string? blank){var cut=Render<HarborlineToaster>(p=>p.Add(x=>x.AccessibleLabel,blank!));Assert.Equal("Notifications",cut.Find("section.hl-toaster").GetAttribute("aria-label"));}
    // A splatted aria-label that is blank or null is absent too, so AccessibleLabel names the region,
    // and with that blank as well the default does. React's twin is its empty, whitespace and null tests.
    [Theory, MemberData(nameof(BlankNames))] public void BlankSplattedAriaLabelFallsBackToAccessibleLabelThenTheDefault(string? blank){var cut=Render<HarborlineToaster>(p=>p.Add(x=>x.AccessibleLabel,"الإشعارات").AddUnmatched("aria-label",blank!));Assert.Equal("الإشعارات",cut.Find("section.hl-toaster").GetAttribute("aria-label"));cut.Render(p=>p.Add(x=>x.AccessibleLabel,blank!).AddUnmatched("aria-label",blank!));Assert.Equal("Notifications",cut.Find("section.hl-toaster").GetAttribute("aria-label"));}
    // A splatted bool or number is absent, as React's false, 0 and 5 tests pin for untyped
    // JavaScript; Blazor would otherwise drop false, write true as an empty name and print 5 as the
    // name. Every .NET numeric type is a JavaScript number or bigint there, so each one is absent.
    public static TheoryData<object> PrimitiveNames => new() { false, true, 0, 5, 5L, 5u, (byte)5, (short)5, 2.5, 2.5f, 5m, (Half)5, (Int128)5, new System.Numerics.BigInteger(5) };
    [Theory, MemberData(nameof(PrimitiveNames))] public void PrimitiveSplattedAriaLabelFallsBackToAccessibleLabel(object value){var cut=Render<HarborlineToaster>(p=>p.AddUnmatched("aria-label",value));Assert.Equal("Notifications",cut.Find("section.hl-toaster").GetAttribute("aria-label"));cut.Render(p=>p.Add(x=>x.AccessibleLabel,"الإشعارات").AddUnmatched("aria-label",value));Assert.Equal("الإشعارات",cut.Find("section.hl-toaster").GetAttribute("aria-label"));}
    // T-1107 review 3: a host that localizes through the splat, aria-label="@L["host.notifications"]",
    // hands the toaster a LocalizedString, not a string. Its text names the region, as Blazor's own
    // element attributes would render it (ToString()), and not English "Notifications". The type
    // check keeps this test honest: it fails if the host stops sending an object.
    [Theory, InlineData("الإشعارات"), InlineData("Benachrichtigungen")]
    public void LocalizedSplattedAriaLabelNamesTheRegion(string translated){Services.AddSingleton<IStringLocalizer<ToasterLocalizedHost>>(new CatalogLocalizer<ToasterLocalizedHost>(new(){["host.notifications"]=translated}));var cut=Render<ToasterLocalizedHost>();Assert.IsType<LocalizedString>(cut.FindComponent<HarborlineToaster>().Instance.AdditionalAttributes!["aria-label"]);Assert.Equal(translated,cut.Find("section.hl-toaster").GetAttribute("aria-label"));}
    // An untranslated blank entry is absent like a blank string: AccessibleLabel names the region,
    // and with that blank too the default does.
    [Fact] public void BlankLocalizedSplattedAriaLabelFallsBackToAccessibleLabelThenTheDefault(){Services.AddSingleton<IStringLocalizer<ToasterLocalizedHost>>(new CatalogLocalizer<ToasterLocalizedHost>(new(){["host.notifications"]=" "}));var cut=Render<ToasterLocalizedHost>(p=>p.Add(x=>x.AccessibleLabel,"الإشعارات"));Assert.Equal("الإشعارات",cut.Find("section.hl-toaster").GetAttribute("aria-label"));cut.Render(p=>p.Add(x=>x.AccessibleLabel,""));Assert.Equal("Notifications",cut.Find("section.hl-toaster").GetAttribute("aria-label"));}
    // Any other object is its ToString() text, a LocalizedString or not, and is absent when that text
    // is blank or null.
    public static TheoryData<object, string> ObjectNames => new() { { new LocalizedString("host.notifications", "Alertes"), "Alertes" }, { new NamedObject("Workspace alerts"), "Workspace alerts" }, { new LocalizedString("host.notifications", ""), "الإشعارات" }, { new LocalizedString("host.notifications", " \t "), "الإشعارات" }, { new NamedObject(null), "الإشعارات" } };
    [Theory, MemberData(nameof(ObjectNames))] public void ObjectSplattedAriaLabelNamesTheRegionByItsText(object value, string expected){var cut=Render<HarborlineToaster>(p=>p.Add(x=>x.AccessibleLabel,"الإشعارات").AddUnmatched("aria-label",value));Assert.Equal(expected,cut.Find("section.hl-toaster").GetAttribute("aria-label"));}
    // T-1107 review 3: the blank check is one character set in both lanes, ECMA-262's WhiteSpace and
    // LineTerminator, the set String.prototype.trim strips. .NET's char.IsWhiteSpace differs at two
    // characters: it counts U+0085 (NEL) as white space and U+FEFF (ZWNBSP) as not. Here, as in React,
    // a NEL-only name is not blank and a ZWNBSP-only one is. React's twin is its U+0085/U+FEFF test.
    public static TheoryData<string, string> WhiteSpaceEdgeNames => new() { { "\u0085", "\u0085" }, { "\u0085 ", "\u0085 " }, { "\uFEFF", "Notifications" }, { " \uFEFF\t\u3000", "Notifications" } };
    [Theory, MemberData(nameof(WhiteSpaceEdgeNames))] public void BlankCheckMatchesReactAtNelAndZwnbsp(string name, string expected){var splatted=Render<HarborlineToaster>(p=>p.AddUnmatched("aria-label",name));Assert.Equal(expected,splatted.Find("section.hl-toaster").GetAttribute("aria-label"));var labelled=Render<HarborlineToaster>(p=>p.Add(x=>x.AccessibleLabel,name));Assert.Equal(expected,labelled.Find("section.hl-toaster").GetAttribute("aria-label"));}
    // The region dir precedence React matches: Direction sets dir, and a caller's own dir attribute,
    // splatted after it, wins over Direction and survives when Direction is unset.
    [Fact] public void RegionDirFollowsDirectionAndTheCallerDir(){var cut=Render<HarborlineToaster>();Assert.False(cut.Find("section.hl-toaster").HasAttribute("dir"));cut.Render(p=>p.Add(x=>x.Direction,"rtl"));Assert.Equal("rtl",cut.Find("section.hl-toaster").GetAttribute("dir"));cut.Render(p=>p.Add(x=>x.Direction,(string?)null).AddUnmatched("dir","rtl"));Assert.Equal("rtl",cut.Find("section.hl-toaster").GetAttribute("dir"));cut.Render(p=>p.Add(x=>x.Direction,"rtl").AddUnmatched("dir","ltr"));Assert.Equal("ltr",cut.Find("section.hl-toaster").GetAttribute("dir"));}
    [Theory,Trait("ModuleConformance","hlp.ui.toaster")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.toaster", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixturePrefix("toaster.");Assert.NotNull(Render<HarborlineToaster>());
        });
    }
    private sealed class NamedObject(string? text){public override string? ToString()=>text;}
    private sealed class CatalogLocalizer<T>(Dictionary<string,string> catalog):IStringLocalizer<T>
    {
        public LocalizedString this[string name]=>catalog.TryGetValue(name,out var value)?new(name,value):new(name,name,resourceNotFound:true);
        public LocalizedString this[string name,params object[] arguments]=>this[name];
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)=>catalog.Select(entry=>new LocalizedString(entry.Key,entry.Value));
    }
    private static void AssertFixturePrefix(string prefix){var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
