namespace Harborline.Kernel.WorkItems;

/// <summary>Half-open contract window in which one versioned definition may accept writes.</summary>
public sealed record DefinitionContractWindow
{
    /// <summary>Creates a validated definition contract window.</summary>
    public DefinitionContractWindow(string definitionId, DateTimeOffset opensAt, DateTimeOffset closesAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        if (closesAt <= opensAt)
            throw new ArgumentException("The contract window must close after it opens.", nameof(closesAt));

        DefinitionId = definitionId;
        OpensAt = opensAt;
        ClosesAt = closesAt;
    }

    /// <summary>Versioned definition identity.</summary>
    public string DefinitionId { get; }
    /// <summary>Inclusive opening instant.</summary>
    public DateTimeOffset OpensAt { get; }
    /// <summary>Exclusive closing instant.</summary>
    public DateTimeOffset ClosesAt { get; }
}

/// <summary>Structured 422 refusal naming the definition contract window.</summary>
public sealed record DefinitionContractWindowRefusal(
    string Code,
    int StatusCode,
    string DefinitionId,
    DateTimeOffset OpensAt,
    DateTimeOffset ClosesAt,
    DateTimeOffset ObservedAt);

/// <summary>Admission result for a definition-backed write.</summary>
public sealed record DefinitionWriteResult<TResult>(
    bool IsAdmitted,
    TResult? Value,
    DefinitionContractWindowRefusal? Refusal);

/// <summary>Stops definition-backed writes outside their declared contract window.</summary>
public static class DefinitionWriteBoundary
{
    /// <summary>Executes a write only while the definition contract window is open.</summary>
    public static async ValueTask<DefinitionWriteResult<TResult>> ExecuteAsync<TResult>(
        DefinitionContractWindow window,
        DateTimeOffset observedAt,
        Func<ValueTask<TResult>> executor)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(executor);

        if (observedAt < window.OpensAt || observedAt >= window.ClosesAt)
        {
            return new(
                false,
                default,
                new DefinitionContractWindowRefusal(
                    "kernel.definition-contract-window",
                    422,
                    window.DefinitionId,
                    window.OpensAt,
                    window.ClosesAt,
                    observedAt));
        }

        return new(true, await executor().ConfigureAwait(false), null);
    }
}
