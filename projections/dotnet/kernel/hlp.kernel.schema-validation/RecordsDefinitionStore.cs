namespace Harborline.Kernel.SchemaValidation;

/// <summary>The lifecycle state captured by one append-only Records definition revision.</summary>
public enum RecordsDefinitionStatus
{
    /// <summary>The semantic version remains author-editable.</summary>
    Draft,
    /// <summary>The semantic version and its compiled schema are immutable.</summary>
    Published,
}

/// <summary>One append-only Records definition lifecycle revision.</summary>
/// <param name="Definition">The immutable definition snapshot.</param>
/// <param name="Revision">The tenant/definition stream revision.</param>
/// <param name="Status">The lifecycle state recorded by this revision.</param>
/// <param name="SchemaId">The registered runtime schema identity for a publication.</param>
/// <param name="RestoredFromVersion">The published version copied into a restored draft.</param>
public sealed record RecordsDefinitionRevision(
    RecordTypeDefinition Definition,
    long Revision,
    RecordsDefinitionStatus Status,
    SchemaId? SchemaId = null,
    string? RestoredFromVersion = null);

/// <summary>A stable lifecycle conflict that leaves Records history unchanged.</summary>
public sealed class RecordsDefinitionConflictException : Exception
{
    /// <summary>Creates a conflict with a stable code.</summary>
    public RecordsDefinitionConflictException(string code, string message, long? currentRevision = null)
        : base(message)
    {
        Code = code;
        CurrentRevision = currentRevision;
    }

    /// <summary>Gets the stable conflict code.</summary>
    public string Code { get; }

    /// <summary>Gets the stream revision observed by a failed expected-revision fence.</summary>
    public long? CurrentRevision { get; }
}

/// <summary>The authoring and publication seam for Records definitions.</summary>
public interface IRecordsDefinitionStore
{
    /// <summary>Appends a new or edited draft when the stream revision matches.</summary>
    ValueTask<RecordsDefinitionRevision> CreateDraftAsync(
        RecordTypeDefinition definition,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    /// <summary>Compiles and immutably publishes a draft when the stream revision matches.</summary>
    ValueTask<RecordsDefinitionRevision> PublishAsync(
        string tenantId,
        string definitionId,
        string version,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    /// <summary>Copies a published snapshot into a new semantic-version draft.</summary>
    ValueTask<RecordsDefinitionRevision> RestoreAsDraftAsync(
        string tenantId,
        string definitionId,
        string sourceVersion,
        string draftVersion,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the highest published semantic version.</summary>
    ValueTask<RecordsDefinitionRevision?> GetPublishedHeadAsync(
        string tenantId,
        string definitionId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every append-only lifecycle revision in stream order.</summary>
    ValueTask<IReadOnlyList<RecordsDefinitionRevision>> ListHistoryAsync(
        string tenantId,
        string definitionId,
        CancellationToken cancellationToken = default);
}

/// <summary>An in-process reference store with expected-revision and replay fencing.</summary>
public sealed class InMemoryRecordsDefinitionStore : IRecordsDefinitionStore, IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly RecordsDefinitionCompiler _compiler;
    private readonly List<RecordsDefinitionRevision> _history = [];
    private readonly Dictionary<(string TenantId, string DefinitionId, string Version), RecordsDefinitionRevision> _versions = [];

    /// <summary>Creates the store over the same registry used for runtime payload validation.</summary>
    public InMemoryRecordsDefinitionStore(ISchemaRegistry schemaRegistry)
    {
        _compiler = new RecordsDefinitionCompiler(schemaRegistry);
    }

    /// <summary>Releases the mutation serializer used by the in-process store.</summary>
    public void Dispose()
    {
        _mutationGate.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async ValueTask<RecordsDefinitionRevision> CreateDraftAsync(
        RecordTypeDefinition definition,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        _ = RecordsSemanticVersion.Parse(definition.Envelope.Version);
        var admission = new RecordsIntentValidator().Validate(definition);
        if (!admission.IsAdmitted)
        {
            throw new RecordsDefinitionAdmissionException(admission.Refusals);
        }

        var snapshot = Snapshot(definition);
        var coordinate = Coordinate(snapshot);
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                var currentRevision = CurrentRevision(snapshot.Envelope.TenantId, snapshot.Envelope.DefinitionId);
                if (_versions.TryGetValue(coordinate, out var existing))
                {
                    if (existing.Status == RecordsDefinitionStatus.Published)
                    {
                        throw Conflict(
                            "records.definition.version_immutable",
                            "A published Records definition version is immutable.",
                            currentRevision);
                    }
                    if (SameContent(existing.Definition, snapshot))
                    {
                        RequireReplayExpected(expectedRevision, existing, currentRevision);
                        return Snapshot(existing);
                    }

                    var editAdmission = new RecordsIntentValidator().Validate(snapshot, existing.Definition);
                    if (!editAdmission.IsAdmitted)
                    {
                        throw new RecordsDefinitionAdmissionException(editAdmission.Refusals);
                    }
                }

                RequireExpected(expectedRevision, currentRevision);
                return Append(
                    snapshot,
                    currentRevision + 1,
                    RecordsDefinitionStatus.Draft);
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<RecordsDefinitionRevision> PublishAsync(
        string tenantId,
        string definitionId,
        string version,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = RecordsSemanticVersion.Parse(version);
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RecordTypeDefinition draft;
            lock (_gate)
            {
                var coordinate = (tenantId, definitionId, version);
                var currentRevision = CurrentRevision(tenantId, definitionId);
                if (!_versions.TryGetValue(coordinate, out var current))
                {
                    throw Conflict(
                        "records.definition.version_not_found",
                        "The Records definition version does not exist.",
                        currentRevision);
                }
                if (current.Status == RecordsDefinitionStatus.Published)
                {
                    RequireReplayExpected(expectedRevision, current, currentRevision);
                    return Snapshot(current);
                }
                RequireExpected(expectedRevision, currentRevision);
                draft = current.Definition;
            }

            var schema = await _compiler.CompileAndRegisterAsync(draft, cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                var currentRevision = CurrentRevision(tenantId, definitionId);
                var coordinate = (tenantId, definitionId, version);
                if (_versions.TryGetValue(coordinate, out var replay)
                    && replay.Status == RecordsDefinitionStatus.Published)
                {
                    RequireReplayExpected(expectedRevision, replay, currentRevision);
                    return Snapshot(replay);
                }
                RequireExpected(expectedRevision, currentRevision);
                return Append(
                    draft,
                    currentRevision + 1,
                    RecordsDefinitionStatus.Published,
                    schema.Id);
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<RecordsDefinitionRevision> RestoreAsDraftAsync(
        string tenantId,
        string definitionId,
        string sourceVersion,
        string draftVersion,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = RecordsSemanticVersion.Parse(sourceVersion);
        _ = RecordsSemanticVersion.Parse(draftVersion);
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                var currentRevision = CurrentRevision(tenantId, definitionId);
                var destination = (tenantId, definitionId, draftVersion);
                if (_versions.TryGetValue(destination, out var existing))
                {
                    if (existing.Status == RecordsDefinitionStatus.Draft
                        && string.Equals(existing.RestoredFromVersion, sourceVersion, StringComparison.Ordinal))
                    {
                        RequireReplayExpected(expectedRevision, existing, currentRevision);
                        return Snapshot(existing);
                    }
                    throw Conflict(
                        "records.definition.revision_conflict",
                        "The restore destination version already exists.",
                        currentRevision);
                }
                if (!_versions.TryGetValue((tenantId, definitionId, sourceVersion), out var source)
                    || source.Status != RecordsDefinitionStatus.Published)
                {
                    throw Conflict(
                        "records.definition.published_source_not_found",
                        "The published Records definition source does not exist.",
                        currentRevision);
                }

                RequireExpected(expectedRevision, currentRevision);
                var restored = Snapshot(source.Definition with
                {
                    Envelope = source.Definition.Envelope with { Version = draftVersion },
                });
                return Append(
                    restored,
                    currentRevision + 1,
                    RecordsDefinitionStatus.Draft,
                    restoredFromVersion: sourceVersion);
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask<RecordsDefinitionRevision?> GetPublishedHeadAsync(
        string tenantId,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var head = _versions.Values
                .Where(revision => revision.Status == RecordsDefinitionStatus.Published
                    && string.Equals(revision.Definition.Envelope.TenantId, tenantId, StringComparison.Ordinal)
                    && string.Equals(revision.Definition.Envelope.DefinitionId, definitionId, StringComparison.Ordinal))
                .OrderByDescending(revision => RecordsSemanticVersion.Parse(revision.Definition.Envelope.Version))
                .FirstOrDefault();
            return ValueTask.FromResult(head is null ? null : Snapshot(head));
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RecordsDefinitionRevision>> ListHistoryAsync(
        string tenantId,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<RecordsDefinitionRevision> history = _history
                .Where(revision => string.Equals(revision.Definition.Envelope.TenantId, tenantId, StringComparison.Ordinal)
                    && string.Equals(revision.Definition.Envelope.DefinitionId, definitionId, StringComparison.Ordinal))
                .OrderBy(revision => revision.Revision)
                .Select(Snapshot)
                .ToArray();
            return ValueTask.FromResult(history);
        }
    }

    private RecordsDefinitionRevision Append(
        RecordTypeDefinition definition,
        long revision,
        RecordsDefinitionStatus status,
        SchemaId? schemaId = null,
        string? restoredFromVersion = null)
    {
        var item = new RecordsDefinitionRevision(
            Snapshot(definition),
            revision,
            status,
            schemaId,
            restoredFromVersion);
        _history.Add(item);
        _versions[Coordinate(definition)] = item;
        return Snapshot(item);
    }

    private long CurrentRevision(string tenantId, string definitionId)
        => _history
            .Where(item => string.Equals(item.Definition.Envelope.TenantId, tenantId, StringComparison.Ordinal)
                && string.Equals(item.Definition.Envelope.DefinitionId, definitionId, StringComparison.Ordinal))
            .Select(item => item.Revision)
            .DefaultIfEmpty(0)
            .Max();

    private static void RequireExpected(long expected, long current)
    {
        if (expected != current)
        {
            throw Conflict(
                "records.definition.expected_revision_conflict",
                $"Expected Records definition revision {expected}, but found {current}.",
                current);
        }
    }

    private static void RequireReplayExpected(
        long expected,
        RecordsDefinitionRevision replay,
        long current)
    {
        if (expected != replay.Revision - 1)
        {
            throw Conflict(
                "records.definition.expected_revision_conflict",
                $"Expected Records definition revision {expected}, but found {current}.",
                current);
        }
    }

    private static RecordsDefinitionConflictException Conflict(
        string code,
        string message,
        long currentRevision)
        => new(code, message, currentRevision);

    private static (string TenantId, string DefinitionId, string Version) Coordinate(
        RecordTypeDefinition definition)
        => (
            definition.Envelope.TenantId,
            definition.Envelope.DefinitionId,
            definition.Envelope.Version);

    private static bool SameContent(RecordTypeDefinition left, RecordTypeDefinition right)
        => string.Equals(
            RecordsDefinitionJson.SerializeCanonical(left),
            RecordsDefinitionJson.SerializeCanonical(right),
            StringComparison.Ordinal);

    private static RecordTypeDefinition Snapshot(RecordTypeDefinition definition)
        => RecordsDefinitionJson.Deserialize(RecordsDefinitionJson.SerializeCanonical(definition));

    private static RecordsDefinitionRevision Snapshot(RecordsDefinitionRevision revision)
        => revision with { Definition = Snapshot(revision.Definition) };

    private sealed record RecordsSemanticVersion(
        string Major,
        string Minor,
        string Patch,
        IReadOnlyList<string> PreRelease) : IComparable<RecordsSemanticVersion>
    {
        public static RecordsSemanticVersion Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw Invalid();
            }
            var buildSplit = value.Split('+', StringSplitOptions.None);
            if (buildSplit.Length > 2
                || (buildSplit.Length == 2 && !ValidIdentifiers(buildSplit[1], numericLeadingZerosAllowed: true)))
            {
                throw Invalid();
            }
            var withoutBuild = buildSplit[0];
            var split = withoutBuild.Split('-', 2, StringSplitOptions.None);
            var core = split[0].Split('.', StringSplitOptions.None);
            if (core.Length != 3
                || !ValidCoreNumber(core[0])
                || !ValidCoreNumber(core[1])
                || !ValidCoreNumber(core[2]))
            {
                throw Invalid();
            }
            var preRelease = split.Length == 1 ? [] : split[1].Split('.');
            if (split.Length == 2
                && !ValidIdentifiers(split[1], numericLeadingZerosAllowed: false))
            {
                throw Invalid();
            }
            return new RecordsSemanticVersion(core[0], core[1], core[2], preRelease);
        }

        public int CompareTo(RecordsSemanticVersion? other)
        {
            if (other is null) return 1;
            var result = CompareNumericIdentifiers(Major, other.Major);
            if (result == 0) result = CompareNumericIdentifiers(Minor, other.Minor);
            if (result == 0) result = CompareNumericIdentifiers(Patch, other.Patch);
            if (result != 0) return result;
            if (PreRelease.Count == 0 || other.PreRelease.Count == 0)
            {
                return PreRelease.Count == other.PreRelease.Count
                    ? 0
                    : PreRelease.Count == 0 ? 1 : -1;
            }
            for (var index = 0; index < Math.Min(PreRelease.Count, other.PreRelease.Count); index++)
            {
                var leftIdentifier = PreRelease[index];
                var rightIdentifier = other.PreRelease[index];
                var leftNumeric = leftIdentifier.All(char.IsAsciiDigit);
                var rightNumeric = rightIdentifier.All(char.IsAsciiDigit);
                var part = (leftNumeric, rightNumeric) switch
                {
                    (true, true) => CompareNumericIdentifiers(leftIdentifier, rightIdentifier),
                    (true, false) => -1,
                    (false, true) => 1,
                    _ => string.CompareOrdinal(leftIdentifier, rightIdentifier),
                };
                if (part != 0) return part;
            }
            return PreRelease.Count.CompareTo(other.PreRelease.Count);
        }

        private static int CompareNumericIdentifiers(string left, string right)
        {
            var length = left.Length.CompareTo(right.Length);
            return length != 0 ? length : string.CompareOrdinal(left, right);
        }

        private static bool ValidCoreNumber(string text)
            => text.Length > 0
                && (text.Length == 1 || text[0] != '0')
                && text.All(char.IsAsciiDigit);

        private static bool ValidIdentifiers(string value, bool numericLeadingZerosAllowed)
            => value.Split('.').All(identifier => identifier.Length > 0
                && identifier.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
                && (numericLeadingZerosAllowed
                    || identifier.Length == 1
                    || identifier[0] != '0'
                    || !identifier.All(char.IsAsciiDigit)));

        private static RecordsDefinitionConflictException Invalid()
            => new(
                "records.definition.version_invalid",
                "The Records definition version is not semantic versioning.");
    }
}
