using Harborline.Kernel.WorkItems;
using Xunit;

namespace Harborline.Kernel.WorkItems.Tests;

public sealed class DefinitionContractWindowTests
{
    private static readonly DateTimeOffset OpensAt = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ClosesAt = OpensAt.AddHours(2);

    [Theory]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task Write_is_admitted_only_inside_definition_contract_window(int hoursFromOpen, bool expectedAdmission)
    {
        var executions = 0;
        var window = new DefinitionContractWindow("records.v1", OpensAt, ClosesAt);

        var result = await DefinitionWriteBoundary.ExecuteAsync(
            window,
            OpensAt.AddHours(hoursFromOpen),
            () =>
            {
                executions++;
                return ValueTask.FromResult("written");
            });

        Assert.Equal(expectedAdmission, result.IsAdmitted);
        Assert.Equal(expectedAdmission ? 1 : 0, executions);
        if (expectedAdmission)
        {
            Assert.Equal("written", result.Value);
            Assert.Null(result.Refusal);
        }
        else
        {
            Assert.Null(result.Value);
            Assert.Equal("kernel.definition-contract-window", result.Refusal?.Code);
            Assert.Equal(422, result.Refusal?.StatusCode);
            Assert.Equal("records.v1", result.Refusal?.DefinitionId);
            Assert.Equal(OpensAt, result.Refusal?.OpensAt);
            Assert.Equal(ClosesAt, result.Refusal?.ClosesAt);
        }
    }
}
