using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Harborline.Foundation.RuleEngine.Standings;

/// <summary>
/// The standing-rule definition store (DES-0018 <c>rules-ck-21</c>). Durable persistence and its DI registration
/// are host adapters (ADR 0096 ruling 5a); this contract and its reference implementation are the producer's.
/// </summary>
public interface IStandingRuleDefinitionStore
{
    /// <summary>Registers an immutable rule version; an identical replay is idempotent, different content refuses.</summary>
    ValueTask RegisterAsync(StandingRuleDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>Gets one rule version, or null.</summary>
    ValueTask<StandingRuleDefinition?> GetAsync(string ruleId, string ruleVersion, CancellationToken cancellationToken = default);

    /// <summary>Removes one rule version; true when it was present.</summary>
    ValueTask<bool> RemoveAsync(string ruleId, string ruleVersion, CancellationToken cancellationToken = default);

    /// <summary>Lists every row in rule identity, then version, order.</summary>
    IAsyncEnumerable<StandingRuleDefinition> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>The in-memory reference store.</summary>
public sealed class InMemoryStandingRuleDefinitionStore : IStandingRuleDefinitionStore
{
    private readonly ConcurrentDictionary<(string RuleId, string Version), StandingRuleDefinition> _rows = new();

    /// <inheritdoc />
    public ValueTask RegisterAsync(StandingRuleDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        var existing = _rows.GetOrAdd((definition.RuleId, definition.RuleVersion), definition);
        if (!ReferenceEquals(existing, definition) && existing.Canonical != definition.Canonical)
            throw new InvalidOperationException($"Standing rule '{definition.RuleId}' version '{definition.RuleVersion}' already has different content.");
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<StandingRuleDefinition?> GetAsync(string ruleId, string ruleVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_rows.GetValueOrDefault((ruleId, ruleVersion)));
    }

    /// <inheritdoc />
    public ValueTask<bool> RemoveAsync(string ruleId, string ruleVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_rows.TryRemove((ruleId, ruleVersion), out _));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StandingRuleDefinition> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var row in _rows.OrderBy(row => row.Key.RuleId, StringComparer.Ordinal).ThenBy(row => row.Key.Version, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row.Value;
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
