using System.Collections.Concurrent;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

public sealed class InMemorySnapshotMarkerSourceTests
{
    [Fact]
    public async Task CaptureAsync_UsesExactFormatAndIsPerInstanceMonotonic()
    {
        var source = new InMemorySnapshotMarkerSource();
        var tenant = new TenantId("tenant-marker");
        Assert.Equal("inmem:tenant-marker:1", await source.CaptureAsync(tenant));
        Assert.Equal("inmem:tenant-marker:2", await source.CaptureAsync(tenant));
        Assert.Equal("inmem:tenant-marker:1", await new InMemorySnapshotMarkerSource().CaptureAsync(tenant));
    }

    [Fact]
    public void CaptureAsync_ConcurrentCapturesAreAllDistinctAndPreserved()
    {
        var source = new InMemorySnapshotMarkerSource();
        var tenant = new TenantId("tenant-concurrent");
        var markers = new ConcurrentBag<string>();
        Parallel.For(0, 100, _ => markers.Add(source.CaptureAsync(tenant).GetAwaiter().GetResult()));
        Assert.Equal(100, markers.Count);
        Assert.Equal(100, markers.Distinct(StringComparer.Ordinal).Count());
    }
}
