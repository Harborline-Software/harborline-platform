using Bunit;
using Microsoft.Extensions.DependencyInjection;
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
    // A splatted value that is not a string is absent, as React's false, 0 and 5 tests pin for
    // untyped JavaScript; Blazor would otherwise drop false and print 5 as the name.
    public static TheoryData<object> NonStringNames => new() { false, 0, 5 };
    [Theory, MemberData(nameof(NonStringNames))] public void NonStringSplattedAriaLabelFallsBackToAccessibleLabel(object value){var cut=Render<HarborlineToaster>(p=>p.AddUnmatched("aria-label",value));Assert.Equal("Notifications",cut.Find("section.hl-toaster").GetAttribute("aria-label"));cut.Render(p=>p.Add(x=>x.AccessibleLabel,"الإشعارات").AddUnmatched("aria-label",value));Assert.Equal("الإشعارات",cut.Find("section.hl-toaster").GetAttribute("aria-label"));}
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
    private static void AssertFixturePrefix(string prefix){var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
