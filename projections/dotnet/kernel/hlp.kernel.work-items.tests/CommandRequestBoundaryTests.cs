using Harborline.Kernel.WorkItems;
using Xunit;

namespace Harborline.Kernel.WorkItems.Tests;

public sealed class CommandRequestBoundaryTests
{
    [Fact]
    public async Task Two_command_request_is_refused_before_executor_runs()
    {
        var executions = 0;
        var commands = new[] { "first", "second" };

        var result = await CommandRequestBoundary.ExecuteAsync(
            commands,
            command =>
            {
                executions++;
                return ValueTask.FromResult(command);
            });

        Assert.False(result.IsAdmitted);
        Assert.Equal("kernel.multi-command-batch", result.Refusal?.Code);
        Assert.Equal(400, result.Refusal?.StatusCode);
        Assert.Equal(2, result.Refusal?.CommandCount);
        Assert.Empty(result.Results);
        Assert.Equal(0, executions);
    }

    [Fact]
    public async Task One_command_request_reaches_executor_once()
    {
        var executions = 0;
        string[] commands = ["only"];

        var result = await CommandRequestBoundary.ExecuteAsync(
            commands,
            command =>
            {
                executions++;
                return ValueTask.FromResult(command.ToUpperInvariant());
            });

        Assert.True(result.IsAdmitted);
        Assert.Null(result.Refusal);
        Assert.Equal(["ONLY"], result.Results);
        Assert.Equal(1, executions);
    }
}
