using System.Text.Json;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class SchedulingInterfaceContractTests
{
    [Fact]
    public void FrozenLedgerVersionMaximumIsTheExactInt64Limit()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Harborline.Platform.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName,
            "specs/modules/blocks/hlp.blocks.scheduling/interface.yaml")));
        var maximum = contract.RootElement.GetProperty("canonicalReferences").GetProperty("limits")
            .GetProperty("ledgerVersion").GetProperty("maximum");
        Assert.Equal("9223372036854775807", maximum.GetRawText());
        Assert.Equal(9223372036854775807L, maximum.GetInt64());
    }
}
