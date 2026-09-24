using Xunit;

namespace Harborline.Foundation.Authorization.Tests;

public sealed class AccessConsumerTests
{
    [Fact]
    public Task Production_consumers_share_filter_check_and_trace_contracts() => AccessContractProof.RunAsync();
}
