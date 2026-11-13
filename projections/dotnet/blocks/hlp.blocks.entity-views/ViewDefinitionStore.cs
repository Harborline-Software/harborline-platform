using System.Text;
using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.RuleEngine.Environments;

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
    ViewBinding Binding,
    ViewDefinitionStatus Status,
    string? RestoredFromVersion = null);

/// <summary>The definition and its authored binding transported together in a signed pack.</summary>
public sealed record ViewDefinitionPackageEntry(
    ViewDefinition Definition,
    ViewBinding Binding);

/// <summary>The authoring and execution store for immutable view-definition revisions.</summary>
public interface IViewDefinitionCatalogue : IViewDefinitionSource
{
    /// <summary>Saves a new draft revision at fresh coordinates; refuses an existing version.</summary>
    ValueTask<ViewDefinitionRevision> CreateDraftAsync(
        ViewDefinition definition,
        ViewBinding binding,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a stored draft. The shared definition store re-admits it with Views admission and derives its record-type
    /// dependency in the same commit (DES-0017 <c>views-eng-16</c>, ADR 0071); a refusal throws
    /// <see cref="DefinitionRefusalException"/> and publishes nothing.
    /// </summary>
    ValueTask<ViewDefinitionRevision> PublishAsync(
        string tenant,
        string key,
        string version,
        CancellationToken cancellationToken = default);

    /// <summary>Copies a published revision into a new draft version.</summary>
    ValueTask<ViewDefinitionRevision> RestoreAsDraftAsync(
        string tenant,
        string key,
        string sourceVersion,
        string draftVersion,
        CancellationToken cancellationToken = default);

    /// <summary>Every version of the definition, one entry each, in semantic-version order.</summary>
    ValueTask<IReadOnlyList<ViewDefinitionRevision>> ListHistoryAsync(
        string tenant,
        string key,
        CancellationToken cancellationToken = default);
}

/// <summary>Builds the definition payload eligible for signed-pack carriage.</summary>
public static class ViewDefinitionPackExporter
{
    public static IReadOnlyList<ViewDefinitionPackageEntry> Export(
        IEnumerable<ViewDefinitionRevision> revisions)
    {
        ArgumentNullException.ThrowIfNull(revisions);
        return revisions
            .Where(revision => revision.Status == ViewDefinitionStatus.Published
                && revision.Definition.Ownership is not ViewOwnershipTier.Personal)
            .Select(revision => new ViewDefinitionPackageEntry(revision.Definition, revision.Binding))
            .OrderBy(entry => entry.Definition.Key, StringComparer.Ordinal)
            .ThenBy(entry => entry.Definition.Version, StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>The host's fixed Views registers, supplied once when the shared definition store is composed.</summary>
/// <param name="Kinds">Every registered view kind.</param>
/// <param name="Measures">Every catalogue measure, as <see cref="MeasureCatalogue"/> registers them at construction.</param>
/// <param name="Functions">The filter-function registry.</param>
/// <param name="Widgets">Every registered widget.</param>
/// <param name="RowActions">Every registered row action.</param>
/// <param name="WorkflowTransitions">Every registered board-move workflow transition.</param>
/// <param name="FieldKinds">
/// Every admitted field-kind revision, as the host's <c>FieldKindRegistry</c> admits them. The publish derivation resolves
/// each shape-role field's pinned kind revision here (T-1177).
/// </param>
/// <param name="Environment">The admitted Views expression environment.</param>
public sealed record ViewAdmissionRegisters(
    IReadOnlyList<ViewKindDescriptor> Kinds,
    IReadOnlyList<ViewMeasureDescriptor> Measures,
    IViewExpressionFunctionRegistry Functions,
    IReadOnlyList<ViewWidgetDescriptor> Widgets,
    IReadOnlyList<string> RowActions,
    IReadOnlyList<string> WorkflowTransitions,
    IReadOnlyList<AdmittedFieldKind> FieldKinds,
    AdmittedEnvironment? Environment = null);

/// <summary>
/// The Views member of the shared definition store (T-620): each view definition is a <see cref="DefinitionKind.Views"/>
/// document whose body is the <see cref="ViewDefinitionDraftJson"/> wire, and whose version id is its semantic version.
/// The host registers <see cref="Admission"/> and <see cref="Dependencies"/>, over the same registers, for <see cref="DefinitionKind.Views"/> when it
/// composes the store, so every draft save and every publish passes Views admission inside the store, whoever calls it
/// (ADR 0071 decision 1; DES-0017 <c>views-eng-16</c>).
/// </summary>
public sealed class ViewDefinitionCatalogue(IVersionedDefinitionStore store) : IViewDefinitionCatalogue
{
    /// <summary>The definition type a Views end of a Definition Dependency names.</summary>
    public const string DefinitionType = "platform.definition-type.view";

    private const string CatalogueMismatch = "definition.catalogue_mismatch";
    private readonly IVersionedDefinitionStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>
    /// The store validator for <see cref="DefinitionKind.Views"/>, pure and synchronous as the store's contract requires
    /// (<see cref="DefinitionAdmission"/>). The body must be the draft wire and name the catalogue's tenant and key, and
    /// every structural rule of <see cref="ViewDefinitionAdmission"/> holds against the fixed <paramref name="registers"/>.
    /// The record type's fields are checked at publication by <see cref="Dependencies"/>, against its published head.
    /// </summary>
    public static DefinitionAdmission Admission(ViewAdmissionRegisters registers)
    {
        ArgumentNullException.ThrowIfNull(registers);
        ViewsExpressionEnvironment.Require(registers.Environment);
        var kinds = registers.Kinds.ToDictionary(kind => kind.Kind, StringComparer.Ordinal);
        var measures = registers.Measures.ToDictionary(measure => measure.Name, StringComparer.Ordinal);
        var widgets = registers.Widgets.ToDictionary(widget => widget.Widget, StringComparer.Ordinal);
        var actions = registers.RowActions.ToHashSet(StringComparer.Ordinal);
        var transitions = registers.WorkflowTransitions.ToHashSet(StringComparer.Ordinal);
        return (document, phase) =>
        {
            ViewsExpressionEnvironment.Require(registers.Environment);
            if (!TryDecode(document, out var draft))
            {
                return [new(ViewDefinitionCodes.BodyInvalid, "")];
            }
            var definition = draft.Definition;
            if (!StringComparer.Ordinal.Equals(definition.Tenant, document.Key.Tenant)
                || !StringComparer.Ordinal.Equals(definition.Key, document.Key.DefinitionId))
            {
                return [new(CatalogueMismatch, "/definition/envelope")];
            }
            var facts = new ViewAdmissionFacts(
                kinds.GetValueOrDefault(draft.Binding.Kind),
                RecordTypeChecked: false,
                RecordType: null,
                definition.Parameters.Measure is { } measure ? measures.GetValueOrDefault(measure.Name) : null,
                registers.Functions,
                draft.Binding.Widget is { } widget ? widgets.GetValueOrDefault(widget.Widget) : null,
                draft.Binding.RowBehavior?.OpenAction is not { } action || actions.Contains(action),
                draft.Binding.BoardMoveTransition is not { } transition || transitions.Contains(transition));
            return ViewDefinitionAdmission.Check(draft, facts)
                .Select(refusal => new DefinitionRefusal(refusal.Code, refusal.Pointer))
                .ToArray();
        };
    }

    /// <summary>
    /// The store's dependency derivation for <see cref="DefinitionKind.Views"/> (DES-0004 §2), registered beside
    /// <see cref="Admission"/> over the same fixed <paramref name="registers"/>, and pure as the store's contract requires. A
    /// published view depends on the Record Type it reads (<c>record_type</c>). A record type with no published head refuses
    /// <see cref="ViewDefinitionCodes.RecordTypeUnresolved"/>, and a column, sort, grouping, filter or shape-role field the
    /// head does not declare refuses <see cref="ViewDefinitionCodes.FieldUnknown"/> or
    /// <see cref="ViewDefinitionCodes.ShapeRoleFieldMissing"/> at its pointer, as Records refuses its own references. A
    /// shape-role field that pins no kind, pins a kind revision not in <see cref="ViewAdmissionRegisters.FieldKinds"/>, or
    /// whose kind (<see cref="ViewBindingCompatibility.KindOf"/>) does not suit its role refuses
    /// <see cref="ViewDefinitionCodes.ShapeRoleFieldIncompatible"/> at <c>/binding/shapeRoles/{role}</c> (T-1177).
    /// </summary>
    public static DefinitionDependencyDerivation Dependencies(ViewAdmissionRegisters registers)
    {
        ArgumentNullException.ThrowIfNull(registers);
        var fieldKinds = registers.FieldKinds.ToDictionary(kind => (kind.KindId, kind.Version));
        return (published, publishedHead) => DeriveDependencies(published, publishedHead, fieldKinds);
    }

    private static IReadOnlyList<DefinitionDependency> DeriveDependencies(DefinitionDocument published,
        Func<DefinitionKey, DefinitionRevision?> publishedHead,
        Dictionary<(string KindId, string Version), AdmittedFieldKind> fieldKinds)
    {
        if (!TryDecode(published, out var draft))
        {
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish, [new(ViewDefinitionCodes.BodyInvalid, "")]);
        }
        var definition = draft.Definition;
        var head = publishedHead(RecordTypeDefinitionStore.KeyOf(published.Key.Tenant, definition.RecordType));
        if (head is null)
        {
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish,
                [new(ViewDefinitionCodes.RecordTypeUnresolved, "/definition/recordType")]);
        }
        var fields = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(head.Document.BodyJson)).Fields
            .Where(field => field.FieldKey is not null)
            .DistinctBy(field => field.FieldKey, StringComparer.Ordinal)
            .ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var declared = fields.Keys.ToHashSet(StringComparer.Ordinal);
        var refusals = ViewDefinitionAdmission.ReferencedFields(definition)
            .Where(reference => !declared.Contains(reference.Field))
            .Select(reference => new DefinitionRefusal(ViewDefinitionCodes.FieldUnknown, reference.Pointer))
            .ToList();
        foreach (var (role, field) in draft.Binding.ShapeRoles.OrderBy(role => role.Key))
        {
            var pointer = $"/binding/shapeRoles/{JsonNamingPolicy.CamelCase.ConvertName(role.ToString())}";
            if (field is null || !fields.TryGetValue(field, out var declaration))
            {
                refusals.Add(new(ViewDefinitionCodes.ShapeRoleFieldMissing, pointer));
                continue;
            }
            // T-1177: the kind is read only from the revision the head pins; a field that pins none has no kind to guess.
            var kind = declaration.Binding?.Kind is { } pinned
                && fieldKinds.TryGetValue((pinned.KindId, pinned.Version), out var admitted)
                ? ViewBindingCompatibility.KindOf(admitted)
                : null;
            if (kind is not { } viewKind || !ViewBindingCompatibility.IsCompatible(role, viewKind))
            {
                refusals.Add(new(ViewDefinitionCodes.ShapeRoleFieldIncompatible, pointer));
            }
        }
        if (refusals.Count > 0)
        {
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish, refusals);
        }
        return [new(
            new(DefinitionType, published.Key.DefinitionId, published.Version),
            new(CatalogueDefinitionType.RecordType, definition.RecordType),
            DefinitionDependencyKind.Derived,
            "record_type")];
    }

    /// <inheritdoc />
    public async ValueTask<ViewDefinitionRevision> CreateDraftAsync(
        ViewDefinition definition,
        ViewBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        _ = ViewSemanticVersion.Parse(definition.Version);
        var key = KeyOf(definition.Tenant, definition.Key);
        var history = await _store.ListHistoryAsync(key, cancellationToken).ConfigureAwait(false);
        if (history.Any(revision => StringComparer.Ordinal.Equals(revision.Document.VersionId, definition.Version)))
        {
            throw new ViewQueryException(ViewDefinitionCodes.RevisionConflict, "The view-definition revision already exists.");
        }
        var body = ViewDefinitionDraftJson.Serialize(new ViewDefinitionDraft(definition, binding));
        var saved = await _store.SaveDraftAsync(new(key, definition.Version, definition.Version, body),
            Head(history), NewRequestId(), cancellationToken).ConfigureAwait(false);
        return Revision(saved, ViewDefinitionStatus.Draft);
    }

    /// <inheritdoc />
    public async ValueTask<ViewDefinitionRevision> PublishAsync(
        string tenant,
        string key,
        string version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definitionKey = KeyOf(tenant, key);
        var history = await _store.ListHistoryAsync(definitionKey, cancellationToken).ConfigureAwait(false);
        if (!history.Any(revision => StringComparer.Ordinal.Equals(revision.Document.VersionId, version)))
        {
            throw new ViewQueryException(ViewDefinitionCodes.NotFound, "The view-definition revision does not exist.");
        }
        var published = await _store.PublishAsync(definitionKey, version, Head(history), NewRequestId(), cancellationToken)
            .ConfigureAwait(false);
        return Revision(published, ViewDefinitionStatus.Published);
    }

    /// <inheritdoc />
    public async ValueTask<ViewDefinitionRevision> RestoreAsDraftAsync(
        string tenant,
        string key,
        string sourceVersion,
        string draftVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = ViewSemanticVersion.Parse(draftVersion);
        var definitionKey = KeyOf(tenant, key);
        var history = await _store.ListHistoryAsync(definitionKey, cancellationToken).ConfigureAwait(false);
        if (!history.Any(revision => StringComparer.Ordinal.Equals(revision.Document.VersionId, sourceVersion)))
        {
            throw new ViewQueryException(ViewDefinitionCodes.NotFound, "The view-definition revision does not exist.");
        }
        if (history.Any(revision => StringComparer.Ordinal.Equals(revision.Document.VersionId, draftVersion)))
        {
            throw new ViewQueryException(ViewDefinitionCodes.RevisionConflict, "The view-definition revision already exists.");
        }
        var restored = await _store.RestoreAsDraftAsync(definitionKey, sourceVersion, draftVersion, draftVersion,
            Head(history), NewRequestId(), cancellationToken).ConfigureAwait(false);
        return Revision(restored, ViewDefinitionStatus.Draft);
    }

    /// <inheritdoc />
    public async ValueTask<ViewDefinition?> ResolvePublishedHeadAsync(
        string tenant,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var head = await _store.GetPublishedHeadAsync(KeyOf(tenant, key), cancellationToken).ConfigureAwait(false);
        return head is null ? null : Revision(head, ViewDefinitionStatus.Published).Definition;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ViewDefinitionRevision>> ListHistoryAsync(
        string tenant,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var history = await _store.ListHistoryAsync(KeyOf(tenant, key), cancellationToken).ConfigureAwait(false);
        return history
            .GroupBy(revision => revision.Document.VersionId, StringComparer.Ordinal)
            .Select(version =>
            {
                var latest = version.Last();
                var restoredFrom = version.First().RestoredFromVersionId;
                var status = version.Any(revision => revision.Status == DefinitionStatus.Published)
                    ? ViewDefinitionStatus.Published
                    : ViewDefinitionStatus.Draft;
                return Revision(latest, status) with { RestoredFromVersion = restoredFrom };
            })
            .OrderBy(revision => ViewSemanticVersion.Parse(revision.Definition.Version))
            .ToArray();
    }

    /// <summary>The shared-store key of a tenant's view definition.</summary>
    public static DefinitionKey KeyOf(string tenant, string key) => new(tenant, DefinitionKind.Views, key);

    private static long Head(IReadOnlyList<DefinitionRevision> history) => history.Count == 0 ? 0 : history.Max(revision => revision.Revision);

    // Views keeps no client replay identity, so each call is its own operation.
    private static string NewRequestId() => Guid.NewGuid().ToString("N");

    // The store's metadata is authoritative for the version: a restore keeps the source body and gives it a new version.
    private static ViewDefinitionRevision Revision(DefinitionRevision revision, ViewDefinitionStatus status)
    {
        var draft = ViewDefinitionDraftJson.Deserialize(revision.Document.BodyJson);
        var definition = draft.Definition with
        {
            Envelope = draft.Definition.Envelope with { Version = revision.Document.Version },
        };
        return new(definition, draft.Binding, status, revision.RestoredFromVersionId);
    }

    private static bool TryDecode(DefinitionDocument document, out ViewDefinitionDraft draft)
    {
        try
        {
            draft = ViewDefinitionDraftJson.Deserialize(document.BodyJson);
            return true;
        }
        catch (JsonException)
        {
            draft = null!;
            return false;
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
                new(ViewDefinitionCodes.VersionInvalid, "The view-definition version is not semantic versioning.");
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
