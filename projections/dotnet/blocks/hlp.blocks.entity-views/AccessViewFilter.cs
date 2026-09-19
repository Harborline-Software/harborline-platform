using System.Text.Json;
using Harborline.Foundation.Authorization;

namespace Harborline.Blocks.EntityViews;

/// <summary>Translates the Views port into the production Access set contract; contains no access policy.</summary>
public sealed class AccessViewFilter(IAccessSetFilter access, string operation) : IViewAccessFilter
{
    public ValueTask<ViewFilter> BuildAsync(string tenant, string principal, string recordType, DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ViewFilter>(new ViewAccessPredicate(access.Bind(operation, principal, tenant, recordType, at), tenant, recordType));
    }
}

/// <summary>A runtime-only predicate, never an authored or serialized visibility rule.</summary>
public sealed record ViewAccessPredicate : ViewFilter
{
    private readonly AccessSetPredicate _predicate;
    private readonly string _tenant, _kind;
    internal ViewAccessPredicate(AccessSetPredicate predicate, string tenant, string kind) =>
        (_predicate, _tenant, _kind) = (predicate, tenant, kind);

    /// <summary>Delegates set admission before the Views row source shapes the result.</summary>
    public ValueTask<IReadOnlyList<ViewRow>> FilterAsync(IEnumerable<ViewRow> rows, CancellationToken cancellationToken = default) =>
        _predicate.FilterAsync(rows, row => new AccessRecord(_tenant, _kind, row.Id,
            row.Values.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToNode(pair.Value), StringComparer.Ordinal)), cancellationToken);
}

/// <summary>Adapts view opening and registered actions to point-of-use checks through Access.</summary>
public sealed class AccessViewOpenGate(AccessProvider access, TimeProvider clock, IReadOnlyList<string> actions) : IViewOpenGate
{
    public async ValueTask<ViewAuthority> AuthorizeAsync(ViewDefinition definition, string principal,
        CancellationToken cancellationToken = default)
    {
        var at = clock.GetUtcNow();
        var record = new AccessRecord(definition.Tenant, "view", definition.Key,
            new Dictionary<string, System.Text.Json.Nodes.JsonNode?>());
        var check = await access.CheckAsync(new(definition.OpenPermission, principal, definition.Tenant, record, at), cancellationToken).ConfigureAwait(false);
        if (!check.Allowed) return new(false, []);
        var authority = new List<ViewRowActionAuthority>();
        foreach (var action in actions)
        {
            var result = await access.CheckAsync(new(action, principal, definition.Tenant, record, at), cancellationToken).ConfigureAwait(false);
            authority.Add(new(action, result.Allowed));
        }
        return new(true, authority);
    }
}
