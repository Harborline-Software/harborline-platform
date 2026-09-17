using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class DataExchangePermissionTests
{
    [Fact]
    public void Capability_vocabulary_is_closed_and_distinguishes_result_reading_from_commit()
    {
        Assert.Equal(
            ["data-exchange:author", "data-exchange:dry-run", "data-exchange:commit", "data-exchange:read-run-results"],
            DataExchangePermissions.All);
        Assert.Equal(4, DataExchangePermissions.All.Distinct(StringComparer.Ordinal).Count());
    }
}
