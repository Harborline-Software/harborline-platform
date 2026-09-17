using System.Collections.Immutable;
using System.Text.Json;

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
    IReadOnlyDictionary<string, string> Parameters,
    string FormatId = "csv");

public sealed record SourceParameterSchema(IReadOnlySet<string> AllowedParameters);

public interface ISourceParameterSchemaRegistry
{
    SourceParameterSchema? Resolve(string capabilityId, string connectorVersion);
}

public sealed class EmptySourceParameterSchemaRegistry : ISourceParameterSchemaRegistry
{
    private static readonly SourceParameterSchema Empty = new(new HashSet<string>(StringComparer.Ordinal));

    public SourceParameterSchema Resolve(string capabilityId, string connectorVersion) => Empty;
}

public enum DataExchangeCascadeLayer
{
    Base,
    Tenant,
}

public sealed record DataExchangeDefinitionRequirement(
    string Capability,
    string? MinimumPlatformVersion = null);

public sealed record DataExchangeDefinitionEnvelope(
    string Identity,
    string Version,
    string Tenant,
    DataExchangeCascadeLayer CascadeLayer,
    JsonElement Provenance,
    IReadOnlyList<DataExchangeDefinitionRequirement> Requires);

public enum MappingMetadataPrecedence
{
    TenantOverPack,
}

public sealed record ReferenceSetBinding(
    string DatasetId,
    string PackDistribution,
    string FeedDistribution);

public sealed record DataExchangeDefinition(
    string Tenant,
    string Key,
    string Version,
    string Title,
    ExchangeSourceBinding Source,
    TabularMappingDocument Mapping,
    ReplayPolicy ReplayPolicy,
    IReadOnlyList<string> ExternalKeyColumns,
    string? RefreshScheduleReference,
    int SchemaVersion = 1,
    DataExchangeDefinitionEnvelope? Envelope = null,
    MappingMetadataPrecedence MetadataPrecedence = MappingMetadataPrecedence.TenantOverPack,
    ReferenceSetBinding? ReferenceSet = null);

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

    ValueTask<DataExchangeDefinitionRevision?> GetPublishedHeadAsync(
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
        Envelope = definition.Envelope is null ? null : definition.Envelope with
        {
            Provenance = definition.Envelope.Provenance.Clone(),
            Requires = definition.Envelope.Requires.ToImmutableArray(),
        },
        ReferenceSet = definition.ReferenceSet is null ? null : definition.ReferenceSet with { },
    };
}

public sealed class InMemoryDataExchangeDefinitionStore : IDataExchangeDefinitionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Tenant, string Key, string Version), DataExchangeDefinitionRevision> _revisions = [];
    private readonly ISourceParameterSchemaRegistry _sourceParameters;

    public InMemoryDataExchangeDefinitionStore(ISourceParameterSchemaRegistry? sourceParameters = null)
    {
        _sourceParameters = sourceParameters ?? new EmptySourceParameterSchemaRegistry();
    }

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

    public ValueTask<DataExchangeDefinitionRevision?> GetPublishedHeadAsync(
        string tenant,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_revisions.Values
                .Where(revision => revision.Definition.Tenant == tenant
                    && revision.Definition.Key == key
                    && revision.Status == DataExchangeDefinitionStatus.Published)
                .OrderByDescending(revision => Version.Parse(revision.Definition.Version))
                .FirstOrDefault());
        }
    }

    private void Validate(DataExchangeDefinition definition)
    {
        _ = Version.Parse(definition.Version);
        _ = TabularMappingAdmission.Validate(definition.Mapping);
        var refusals = new List<DataExchangeRefusal>();
        if (definition.SchemaVersion != 1)
        {
            refusals.Add(new("definition.schema_version_unsupported", "/schemaVersion"));
        }
        if (definition.Envelope is not null
            && (definition.Envelope.Identity != definition.Key
                || definition.Envelope.Version != definition.Version
                || definition.Envelope.Tenant != definition.Tenant))
        {
            refusals.Add(new("definition.envelope_mismatch", "/envelope"));
        }
        if (string.IsNullOrWhiteSpace(definition.Source.FormatId))
        {
            refusals.Add(new("definition.format_required", "/source/formatId"));
        }
        if (!IsSecretReference(definition.Source.SecretReference))
        {
            refusals.Add(new("definition.secret_reference_invalid", "/source/secretReference"));
        }
        var parameterSchema = _sourceParameters.Resolve(
            definition.Source.CapabilityId,
            definition.Source.ConnectorVersion);
        if (parameterSchema is null)
        {
            refusals.Add(new("definition.source_capability_unregistered", "/source/capabilityId"));
        }
        foreach (var parameter in definition.Source.Parameters.Keys)
        {
            if (parameterSchema is not null && !parameterSchema.AllowedParameters.Contains(parameter))
            {
                refusals.Add(new("definition.source_parameter_undeclared", $"/source/parameters/{parameter}"));
            }
            var normalized = new string(parameter.Where(char.IsLetterOrDigit).ToArray());
            if (normalized.Contains("password", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("credential", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("apikey", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("accesstoken", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("clientsecret", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("token", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("secret", StringComparison.OrdinalIgnoreCase))
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

    private static bool IsSecretReference(string value)
    {
        const string secretScheme = "secret://";
        const string referenceScheme = "secretref:";
        var remainder = value.StartsWith(secretScheme, StringComparison.Ordinal)
            ? value[secretScheme.Length..]
            : value.StartsWith(referenceScheme, StringComparison.Ordinal)
                ? value[referenceScheme.Length..]
                : string.Empty;
        return remainder.Length > 0
            && remainder.All(character => char.IsLetterOrDigit(character)
                || character is '.' or '_' or ':' or '/' or '-');
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
