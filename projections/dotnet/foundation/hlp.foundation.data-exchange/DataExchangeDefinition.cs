using System.Collections.Immutable;

namespace Harborline.Foundation.DataExchange;

public enum ReplayPolicy
{
    Append,
    Overwrite,
    AppendDeduplicate,
}

public sealed record ExchangeSourceBinding(
    string CapabilityId,
    string ConnectorVersion,
    string SecretReference,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record DataExchangeDefinition(
    string Tenant,
    string Key,
    string Version,
    string Title,
    ExchangeSourceBinding Source,
    TabularMappingDocument Mapping,
    ReplayPolicy ReplayPolicy,
    IReadOnlyList<string> ExternalKeyColumns,
    string? RefreshScheduleReference);

public enum DataExchangeDefinitionStatus
{
    Draft,
    Published,
    Withdrawn,
}

public sealed record DataExchangeDefinitionRevision(
    DataExchangeDefinition Definition,
    DataExchangeDefinitionStatus Status,
    string? RestoredFromVersion = null);

public sealed record DataExchangeDefinitionPackageEntry(DataExchangeDefinition Definition);

public interface IDataExchangeDefinitionStore
{
    ValueTask<DataExchangeDefinitionRevision> CreateDraftAsync(
        DataExchangeDefinition definition,
        CancellationToken cancellationToken = default);

    ValueTask<DataExchangeDefinitionRevision> PublishAsync(
        string tenant,
        string key,
        string version,
        CancellationToken cancellationToken = default);

    ValueTask<DataExchangeDefinitionRevision> RestoreAsDraftAsync(
        string tenant,
        string key,
        string sourceVersion,
        string draftVersion,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<DataExchangeDefinitionRevision>> ListHistoryAsync(
        string tenant,
        string key,
        CancellationToken cancellationToken = default);
}

public static class DataExchangeDefinitionPackExporter
{
    public static IReadOnlyList<DataExchangeDefinitionPackageEntry> Export(
        IEnumerable<DataExchangeDefinitionRevision> revisions)
    {
        ArgumentNullException.ThrowIfNull(revisions);
        return revisions
            .Where(revision => revision.Status == DataExchangeDefinitionStatus.Published)
            .Select(revision => new DataExchangeDefinitionPackageEntry(Snapshot(revision.Definition)))
            .OrderBy(entry => entry.Definition.Key, StringComparer.Ordinal)
            .ThenBy(entry => Version.Parse(entry.Definition.Version))
            .ToArray();
    }

    internal static DataExchangeDefinition Snapshot(DataExchangeDefinition definition) => definition with
    {
        Source = definition.Source with
        {
            Parameters = definition.Source.Parameters.ToImmutableDictionary(StringComparer.Ordinal),
        },
        Mapping = definition.Mapping with
        {
            Target = definition.Mapping.Target with { },
            Columns = definition.Mapping.Columns.Select(column => column with
            {
                Null = column.Null?.ToImmutableArray(),
                Extensions = column.Extensions?.ToImmutableDictionary(StringComparer.Ordinal),
            }).ToImmutableArray(),
            Extensions = definition.Mapping.Extensions.ToImmutableDictionary(StringComparer.Ordinal),
        },
        ExternalKeyColumns = definition.ExternalKeyColumns.ToImmutableArray(),
    };
}

public sealed class InMemoryDataExchangeDefinitionStore : IDataExchangeDefinitionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Tenant, string Key, string Version), DataExchangeDefinitionRevision> _revisions = [];

    public ValueTask<DataExchangeDefinitionRevision> CreateDraftAsync(
        DataExchangeDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        Validate(definition);
        var revision = new DataExchangeDefinitionRevision(
            DataExchangeDefinitionPackExporter.Snapshot(definition),
            DataExchangeDefinitionStatus.Draft);
        lock (_gate)
        {
            Add(revision);
        }
        return ValueTask.FromResult(revision);
    }

    public ValueTask<DataExchangeDefinitionRevision> PublishAsync(
        string tenant,
        string key,
        string version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var coordinates = (tenant, key, version);
            if (!_revisions.TryGetValue(coordinates, out var revision))
            {
                throw new ExchangeRunConflictException("The definition revision does not exist.");
            }
            var published = revision with { Status = DataExchangeDefinitionStatus.Published };
            _revisions[coordinates] = published;
            return ValueTask.FromResult(published);
        }
    }

    public ValueTask<DataExchangeDefinitionRevision> RestoreAsDraftAsync(
        string tenant,
        string key,
        string sourceVersion,
        string draftVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = Version.Parse(draftVersion);
        lock (_gate)
        {
            if (!_revisions.TryGetValue((tenant, key, sourceVersion), out var source))
            {
                throw new ExchangeRunConflictException("The source definition revision does not exist.");
            }
            var restored = new DataExchangeDefinitionRevision(
                DataExchangeDefinitionPackExporter.Snapshot(source.Definition with { Version = draftVersion }),
                DataExchangeDefinitionStatus.Draft,
                sourceVersion);
            Add(restored);
            return ValueTask.FromResult(restored);
        }
    }

    public ValueTask<IReadOnlyList<DataExchangeDefinitionRevision>> ListHistoryAsync(
        string tenant,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<DataExchangeDefinitionRevision> result = _revisions.Values
                .Where(revision => revision.Definition.Tenant == tenant && revision.Definition.Key == key)
                .OrderBy(revision => Version.Parse(revision.Definition.Version))
                .ToArray();
            return ValueTask.FromResult(result);
        }
    }

    private static void Validate(DataExchangeDefinition definition)
    {
        _ = Version.Parse(definition.Version);
        _ = TabularMappingAdmission.Validate(definition.Mapping);
        var refusals = new List<DataExchangeRefusal>();
        foreach (var parameter in definition.Source.Parameters.Keys)
        {
            var normalized = parameter.Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal);
            if (normalized.Contains("password", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("credential", StringComparison.OrdinalIgnoreCase))
            {
                refusals.Add(new("definition.credential_forbidden", $"/source/parameters/{parameter}"));
            }
            if (normalized.Contains("cursor", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("checkpoint", StringComparison.OrdinalIgnoreCase))
            {
                refusals.Add(new("definition.cursor_forbidden", $"/source/parameters/{parameter}"));
            }
            if (normalized.Contains("retention", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("retainuntil", StringComparison.OrdinalIgnoreCase))
            {
                refusals.Add(new("definition.retention_forbidden", $"/source/parameters/{parameter}"));
            }
        }
        if (definition.ReplayPolicy == ReplayPolicy.AppendDeduplicate
            && definition.ExternalKeyColumns.Count == 0)
        {
            refusals.Add(new("definition.external_key_required", "/externalKeyColumns"));
        }
        var mappedColumns = definition.Mapping.Columns
            .Select(column => column.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var externalKey in definition.ExternalKeyColumns.Where(column => !mappedColumns.Contains(column)))
        {
            refusals.Add(new("definition.external_key_unknown", $"/externalKeyColumns/{externalKey}"));
        }
        if (refusals.Count > 0)
        {
            throw new DataExchangeAdmissionException(refusals);
        }
    }

    private void Add(DataExchangeDefinitionRevision revision)
    {
        var definition = revision.Definition;
        if (!_revisions.TryAdd((definition.Tenant, definition.Key, definition.Version), revision))
        {
            throw new ExchangeRunConflictException("The definition revision already exists.");
        }
    }
}
