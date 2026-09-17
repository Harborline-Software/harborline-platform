namespace Harborline.Blocks.EntityViews;

/// <summary>The lifecycle state of one immutable view-definition revision.</summary>
public enum ViewDefinitionStatus
{
    Draft,
    Published,
    Withdrawn,
}

/// <summary>One append-only definition revision and its lifecycle metadata.</summary>
public sealed record ViewDefinitionRevision(
    ViewDefinition Definition,
    ViewDefinitionStatus Status,
    string? RestoredFromVersion = null);

/// <summary>The authoring and execution store for immutable view-definition revisions.</summary>
public interface IViewDefinitionStore : IViewDefinitionSource
{
    ValueTask<ViewDefinitionRevision> CreateDraftAsync(
        ViewDefinition definition,
        CancellationToken cancellationToken = default);

    ValueTask<ViewDefinitionRevision> PublishAsync(
        string tenant,
        string key,
        string version,
        CancellationToken cancellationToken = default);

    ValueTask<ViewDefinitionRevision> RestoreAsDraftAsync(
        string tenant,
        string key,
        string sourceVersion,
        string draftVersion,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ViewDefinitionRevision>> ListHistoryAsync(
        string tenant,
        string key,
        CancellationToken cancellationToken = default);
}

/// <summary>Builds the definition payload eligible for signed-pack carriage.</summary>
public static class ViewDefinitionPackExporter
{
    public static IReadOnlyList<ViewDefinition> Export(
        IEnumerable<ViewDefinitionRevision> revisions)
    {
        ArgumentNullException.ThrowIfNull(revisions);
        return revisions
            .Where(revision => revision.Status == ViewDefinitionStatus.Published
                && revision.Definition.Ownership is not ViewOwnershipTier.Personal)
            .Select(revision => revision.Definition)
            .OrderBy(definition => definition.Key, StringComparer.Ordinal)
            .ThenBy(definition => definition.Version, StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>An in-process reference store with immutable coordinates and semver head resolution.</summary>
public sealed class InMemoryViewDefinitionStore : IViewDefinitionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Tenant, string Key, string Version), ViewDefinitionRevision> _revisions = [];

    public ValueTask<ViewDefinitionRevision> CreateDraftAsync(
        ViewDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        _ = ViewSemanticVersion.Parse(definition.Version);

        var revision = new ViewDefinitionRevision(definition, ViewDefinitionStatus.Draft);
        lock (_gate)
        {
            Add(revision);
        }
        return ValueTask.FromResult(revision);
    }

    public ValueTask<ViewDefinitionRevision> PublishAsync(
        string tenant,
        string key,
        string version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var coordinates = (tenant, key, version);
            if (!_revisions.TryGetValue(coordinates, out var current))
            {
                throw new ViewQueryException("view_definition.not_found", "The view-definition revision does not exist.");
            }
            var published = current with { Status = ViewDefinitionStatus.Published };
            _revisions[coordinates] = published;
            return ValueTask.FromResult(published);
        }
    }

    public ValueTask<ViewDefinitionRevision> RestoreAsDraftAsync(
        string tenant,
        string key,
        string sourceVersion,
        string draftVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = ViewSemanticVersion.Parse(draftVersion);
        lock (_gate)
        {
            if (!_revisions.TryGetValue((tenant, key, sourceVersion), out var source))
            {
                throw new ViewQueryException("view_definition.not_found", "The view-definition revision does not exist.");
            }
            var restoredDefinition = source.Definition with
            {
                Envelope = source.Definition.Envelope with { Version = draftVersion },
            };
            var restored = new ViewDefinitionRevision(
                restoredDefinition,
                ViewDefinitionStatus.Draft,
                sourceVersion);
            Add(restored);
            return ValueTask.FromResult(restored);
        }
    }

    public ValueTask<ViewDefinition?> ResolvePublishedHeadAsync(
        string tenant,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var head = _revisions.Values
                .Where(revision => revision.Status == ViewDefinitionStatus.Published
                    && StringComparer.Ordinal.Equals(revision.Definition.Tenant, tenant)
                    && StringComparer.Ordinal.Equals(revision.Definition.Key, key))
                .OrderByDescending(revision => ViewSemanticVersion.Parse(revision.Definition.Version))
                .FirstOrDefault();
            return ValueTask.FromResult(head?.Definition);
        }
    }

    public ValueTask<IReadOnlyList<ViewDefinitionRevision>> ListHistoryAsync(
        string tenant,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<ViewDefinitionRevision> history = _revisions.Values
                .Where(revision => StringComparer.Ordinal.Equals(revision.Definition.Tenant, tenant)
                    && StringComparer.Ordinal.Equals(revision.Definition.Key, key))
                .OrderBy(revision => ViewSemanticVersion.Parse(revision.Definition.Version))
                .ToArray();
            return ValueTask.FromResult(history);
        }
    }

    private void Add(ViewDefinitionRevision revision)
    {
        var definition = revision.Definition;
        if (!_revisions.TryAdd((definition.Tenant, definition.Key, definition.Version), revision))
        {
            throw new ViewQueryException("view_definition.revision_conflict", "The view-definition revision already exists.");
        }
    }

    private sealed record ViewSemanticVersion(
        int Major,
        int Minor,
        int Patch,
        IReadOnlyList<string> PreRelease) : IComparable<ViewSemanticVersion>
    {
        public static ViewSemanticVersion Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw Invalid();
            }
            var withoutBuild = value.Split('+', 2, StringSplitOptions.None)[0];
            var split = withoutBuild.Split('-', 2, StringSplitOptions.None);
            var core = split[0].Split('.', StringSplitOptions.None);
            if (core.Length != 3
                || !TryNumber(core[0], out var major)
                || !TryNumber(core[1], out var minor)
                || !TryNumber(core[2], out var patch))
            {
                throw Invalid();
            }
            var pre = split.Length == 1 ? [] : split[1].Split('.');
            if (pre.Any(identifier => identifier.Length == 0
                    || identifier.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')
                    || (identifier.Length > 1 && identifier[0] == '0' && identifier.All(char.IsDigit))))
            {
                throw Invalid();
            }
            return new(major, minor, patch, pre);

            static bool TryNumber(string text, out int number)
            {
                number = 0;
                return text.Length > 0
                    && (text.Length == 1 || text[0] != '0')
                    && int.TryParse(
                        text,
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out number);
            }

            static ViewQueryException Invalid() =>
                new("view_definition.version_invalid", "The view-definition version is not semantic versioning.");
        }

        public int CompareTo(ViewSemanticVersion? other)
        {
            if (other is null) return 1;
            var core = Major.CompareTo(other.Major);
            if (core == 0) core = Minor.CompareTo(other.Minor);
            if (core == 0) core = Patch.CompareTo(other.Patch);
            if (core != 0) return core;
            if (PreRelease.Count == 0 || other.PreRelease.Count == 0)
            {
                return PreRelease.Count == other.PreRelease.Count ? 0 : PreRelease.Count == 0 ? 1 : -1;
            }
            for (var index = 0; index < Math.Min(PreRelease.Count, other.PreRelease.Count); index++)
            {
                var leftNumeric = int.TryParse(PreRelease[index], out var left);
                var rightNumeric = int.TryParse(other.PreRelease[index], out var right);
                var part = (leftNumeric, rightNumeric) switch
                {
                    (true, true) => left.CompareTo(right),
                    (true, false) => -1,
                    (false, true) => 1,
                    _ => string.CompareOrdinal(PreRelease[index], other.PreRelease[index]),
                };
                if (part != 0) return part;
            }
            return PreRelease.Count.CompareTo(other.PreRelease.Count);
        }
    }
}
