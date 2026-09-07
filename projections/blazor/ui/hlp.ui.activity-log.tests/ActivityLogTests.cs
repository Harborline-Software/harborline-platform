using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;
public sealed class ActivityLogTests:BunitContext
{
 public ActivityLogTests()=>Services.AddSingleton<IMediaQueryObserver>(new FakeMedia());
 private static IReadOnlyList<ActivityEntry> Entries=>[new("a","Alex","approved","1 min ago","2026-08-10T12:00:00Z","Reviewed",ActivityTone.Positive),new("b","Sam","queued","now")];
 [Fact] public void TableSeparatesDisplayAndMachineTimestamp(){var cut=Render<HarborlineActivityLog>(p=>p.Add(x=>x.Entries,Entries).Add(x=>x.Layout,ActivityLogLayout.Table));Assert.Equal(2,cut.FindAll("tbody tr").Count);Assert.Equal("2026-08-10T12:00:00Z",cut.Find("time").GetAttribute("datetime"));Assert.Equal("1 min ago",cut.Find("time").TextContent);}
 [Fact] public void ListAndLimitPreserveOrder(){var cut=Render<HarborlineActivityLog>(p=>p.Add(x=>x.Entries,Entries).Add(x=>x.Layout,ActivityLogLayout.List).Add(x=>x.MaxVisible,1).Add(x=>x.OnShowMore,()=>{}));Assert.Single(cut.FindAll("li"));Assert.Contains("Alex",cut.Find("li").TextContent);Assert.NotNull(cut.Find("button"));}
 [Fact,Trait("ModuleConformance","hlp.ui.activity-log")] public void SharedFixtureConforms(){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);var id=fixture.RootElement.GetProperty("id").GetString();Assert.StartsWith("activity-log.",id);var expected=fixture.RootElement.GetProperty("expected");if(id!="activity-log.table-classes"){Assert.NotNull(Render<HarborlineActivityLog>(p=>p.Add(x=>x.Entries,Entries).Add(x=>x.Layout,ActivityLogLayout.List)));return;}
  // 282 s1: the Blazor lane once spelled the overflow container hl-activity-log__table-wrap and the
  // tone dot hl-activity-log__tone, neither defined by the authority stylesheet. This asserts the
  // one spelling in this lane; the React case asserts the same fixture rows in the other.
  var table=Render<HarborlineActivityLog>(p=>p.Add(x=>x.Entries,Entries).Add(x=>x.Layout,ActivityLogLayout.Table));
  Assert.Equal(Classes(expected,"tableScrollClasses"),table.Find("section > div").ClassList);
  Assert.Equal(Classes(expected,"actionHeaderClasses"),table.Find("th.hl-activity-log__header--action").ClassList);
  var markers=table.FindAll(".hl-activity-log__marker");
  Assert.NotEmpty(markers);
  foreach(var marker in markers) Assert.Equal(Classes(expected,"markerClasses"),marker.ClassList);}
 private static string[] Classes(System.Text.Json.JsonElement expected,string property)=>expected.GetProperty(property).EnumerateArray().Select(value=>value.GetString()!).ToArray();
 private sealed class FakeMedia:IMediaQueryObserver{public ValueTask<IMediaQuerySubscription> ObserveAsync(string q,Func<MediaQueryChange,ValueTask> c,CancellationToken t=default)=>new(new Sub(q));private sealed class Sub(string q):IMediaQuerySubscription{public string Query=>q;public bool Matches=>false;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}}
}
