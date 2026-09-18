namespace Harborline.Kernel.WorkItems;

/// <summary>Structured refusal returned before a multi-command request can reach an executor.</summary>
public sealed record CommandBatchRefusal(string Code, int StatusCode, int CommandCount);

/// <summary>Admission result for one request-scoped command execution.</summary>
public sealed record CommandRequestResult<TResult>(
    bool IsAdmitted,
    IReadOnlyList<TResult> Results,
    CommandBatchRefusal? Refusal);

/// <summary>Enforces one command per request before dispatching any command.</summary>
public static class CommandRequestBoundary
{
    /// <summary>Refuses requests carrying more than one command without invoking the executor.</summary>
    public static async ValueTask<CommandRequestResult<TResult>> ExecuteAsync<TCommand, TResult>(
        IEnumerable<TCommand> commands,
        Func<TCommand, ValueTask<TResult>> executor)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(executor);

        var materialized = commands as IReadOnlyList<TCommand> ?? commands.ToArray();
        if (materialized.Count > 1)
        {
            return new(
                false,
                Array.Empty<TResult>(),
                new CommandBatchRefusal("kernel.multi-command-batch", 400, materialized.Count));
        }

        if (materialized.Count == 0)
            return new(true, Array.Empty<TResult>(), null);

        var result = await executor(materialized[0]).ConfigureAwait(false);
        return new(true, new[] { result }, null);
    }
}
