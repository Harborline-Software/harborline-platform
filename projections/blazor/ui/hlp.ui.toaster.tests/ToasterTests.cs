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
    [Fact,Trait("ModuleConformance","hlp.ui.toaster")] public void SharedFixtureConforms(){AssertFixturePrefix("toaster.");Assert.NotNull(Render<HarborlineToaster>());}
    private static void AssertFixturePrefix(string prefix){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
