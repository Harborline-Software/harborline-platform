using Harborline.Foundation.Definitions;
using Xunit;

namespace Harborline.Foundation.Definitions.Tests;

// The ruled contract window (T-572 rulings 85, 86, 88): the pure check every envelope admission will call.
public sealed class DefinitionContractCheckTests
{
    [Theory]
    [MemberData(nameof(WindowCases))]
    public void Check_applies_the_ruled_contract_window(
        DefinitionContractWindow window,
        DefinitionContractVersion? declared,
        string? expectedCode)
    {
        var refusal = window.Check(declared, "definition@1.0");

        Assert.Equal(expectedCode, refusal?.Code);
        if (refusal is not null)
        {
            Assert.Equal("/envelope/contract", refusal.Pointer);
            Assert.Equal("definition@1.0", refusal.Target);
        }
    }

    public static TheoryData<DefinitionContractWindow, DefinitionContractVersion?, string?> WindowCases => new()
    {
        { new(2, 3, 1), new(3, 0), "definition.contract.out_of_window" },
        { new(2, 3, 1), new(0, 0), "definition.contract.out_of_window" },
        { new(2, 3, 1), new(1, 0), null },
        { new(2, 3, 1), new(1, int.MaxValue), null },
        { new(2, 3, 1), new(2, 2), null },
        { new(2, 3, 1), new(2, 3), null },
        { new(2, 3, 1), new(2, 4), "definition.contract.out_of_window" },
        { new(0, 3, 0), new(0, 2), "definition.contract.out_of_window" },
        { new(0, 3, 0), new(0, 3), null },
        { new(0, 3, 0), new(0, 4), "definition.contract.out_of_window" },
        { new(1, 0, 1), null, "definition.contract.missing" },
        { new(1, 0, 1), new(-1, 0), "definition.contract.missing" },
        { new(1, 0, 1), new(1, -1), "definition.contract.missing" },
    };

}
