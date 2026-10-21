using Harborline.Contracts.Authorization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms;

/// <summary>
/// In-memory reference implementation of <see cref="IFormDefinitionStore"/>.
/// Suitable for tests, single-process bootstrapping, and the early
/// authoring-UX iteration loop; the production Postgres-backed
/// implementation ships in a follow-up PR that composes the
/// <c>foundation-assets-postgres</c> JSONB entity-store extension
/// (per ADR 0055 §"Entity Store").
/// </summary>
/// <remarks>
/// <para>
/// Storage shape: a nested dictionary keyed by tenant → schema id →
/// version → record. Mutation is serialised by a single
/// <see cref="SemaphoreSlim"/>; the dictionary itself is replaced on
/// each mutation so iteration over a snapshot is lock-free. The shape
/// keeps the implementation small enough for the keystone PR while
/// preserving the contract semantics that consumers will rely on once
/// the production implementation lands.
/// </para>
/// </remarks>
public sealed class InMemoryFormDefinitionStore : IFormDefinitionStore, IDisposable
{
    private readonly SemaphoreSlim _mutationLock = new(initialCount: 1, maxCount: 1);
    private readonly TimeProvider _time;
    private readonly AuthorizationCapabilityRegister? _capabilities;
    private readonly IFormRecordTypePolicyReader? _recordTypePolicies;
    private readonly IFormRecordCreationDeclarations? _recordCreations;

    // tenant → id → version → definition
    private Dictionary<TenantId, Dictionary<FormDefinitionId, Dictionary<SemanticVersion, FormDefinition>>> _store = new();

    /// <summary>Constructs a registry with the supplied <see cref="TimeProvider"/>.</summary>
    /// <param name="time">Time source for lifecycle-transition timestamps.
    /// Pass <see cref="TimeProvider.System"/> in production; pass a test
    /// double for deterministic clocks in tests.</param>
    /// <param name="capabilities">Access's capability register a submit gate's capability arm resolves
    /// through (forms-auth-16, T-747). Absent, every capability arm is refused.</param>
    /// <param name="recordTypePolicies">The host's lookup of a targeted record type's creation gate (T-485, D3, K6).
    /// Absent, publishing any definition with a <c>record_type_id</c> target refuses with
    /// <c>form.record_type.policy_unavailable</c>; Forms never defaults a policy.</param>
    /// <param name="recordCreations">The host producers' declaration of which Record Types a form's submissions create (K9).
    /// When it declares a type for a form id, publishing a revision of that form without that exact target refuses
    /// (<c>form.record_type.target_required</c>, <c>form.record_type.target_mismatch</c>); a failing or ambiguous declaration
    /// refuses (<c>form.record_type.policy_unavailable</c>, <c>form.record_type.target_ambiguous</c>). Absent, no form is
    /// declared to create Records.</param>
    /// <param name="recordCreationProducer">The host's submission producer, for example the projection sink the form engine
    /// is given, typed as the declaration it must implement (N2): passing a producer that declares nothing is a compile error
    /// rather than a silent absence of any declaration. When <paramref name="recordCreations"/> is null its declaration is
    /// used: the same fallback the form engine applies to its sink (<see cref="RecordCreationDeclarations.Select"/>), so
    /// publication and submit refuse consistently.</param>
    public InMemoryFormDefinitionStore(
        TimeProvider time,
        AuthorizationCapabilityRegister? capabilities = null,
        IFormRecordTypePolicyReader? recordTypePolicies = null,
        IFormRecordCreationDeclarations? recordCreations = null,
        IFormRecordCreationDeclarations? recordCreationProducer = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _capabilities = capabilities;
        _recordTypePolicies = recordTypePolicies;
        _recordCreations = RecordCreationDeclarations.Select(recordCreations, recordCreationProducer);
    }

    /// <summary>Constructs a registry using <see cref="TimeProvider.System"/>.</summary>
    public InMemoryFormDefinitionStore() : this(TimeProvider.System)
    {
    }

    /// <inheritdoc />
    public ValueTask<FormDefinition> GetAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default)
    {
        var snapshot = _store;
        if (snapshot.TryGetValue(tenant, out var byId) &&
            byId.TryGetValue(id, out var byVersion) &&
            byVersion.TryGetValue(version, out var schema))
        {
            return new ValueTask<FormDefinition>(schema);
        }

        throw new FormDefinitionNotFoundException(id, version, tenant);
    }

    /// <inheritdoc />
    public ValueTask<FormDefinition?> GetCurrentPublishedAsync(TenantId tenant, FormDefinitionId id, CancellationToken ct = default)
    {
        var snapshot = _store;
        if (!snapshot.TryGetValue(tenant, out var byId) || !byId.TryGetValue(id, out var byVersion))
        {
            return new ValueTask<FormDefinition?>((FormDefinition?)null);
        }

        FormDefinition? best = null;
        foreach (var revision in byVersion.Values)
        {
            if (revision.Status != FormDefinitionStatus.Published) continue;
            if (best is null || revision.Version > best.Version)
            {
                best = revision;
            }
        }

        return new ValueTask<FormDefinition?>(best);
    }

    /// <inheritdoc />
    public async ValueTask<FormDefinition> RegisterAsync(FormDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await ValidateDefinitionAsync(definition, requireSubmitGate: definition.Status == FormDefinitionStatus.Published, ct).ConfigureAwait(false);

        await _mutationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = _store;
            if (snapshot.TryGetValue(definition.Tenant, out var byId) &&
                byId.TryGetValue(definition.Id, out var byVersion) &&
                byVersion.ContainsKey(definition.Version))
            {
                throw new FormDefinitionConflictException(definition.Id, definition.Version, definition.Tenant);
            }

            // Optional lineage validation: parent MUST exist in the same tenant.
            if (definition.Lineage is { } lineage)
            {
                if (!snapshot.TryGetValue(definition.Tenant, out var lineageById) ||
                    !lineageById.TryGetValue(lineage.ParentDefinitionId, out var lineageByVersion) ||
                    !lineageByVersion.ContainsKey(lineage.ParentVersion))
                {
                    throw new FormDefinitionValidationException(
                        definition.Id,
                        $"lineage references parent '{lineage.ParentDefinitionId}' at version '{lineage.ParentVersion}' which is not registered in tenant '{definition.Tenant}'.");
                }
            }

            if (definition.Status == FormDefinitionStatus.Published)
                FormDefinitionValidation.ValidateRecordTypeContinuityOrThrow(definition, Revisions(snapshot, definition.Tenant, definition.Id));
            _store = MutateStore(snapshot, definition);
            return definition;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<FormDefinition> RegisterAndPublishAsync(
        FormDefinition definition,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await ValidateDefinitionAsync(definition, requireSubmitGate: true, ct).ConfigureAwait(false);
        if (definition.Status != FormDefinitionStatus.Draft)
        {
            throw new FormDefinitionValidationException(
                definition.Id,
                $"atomic registration and publication requires Draft status, not {definition.Status}.");
        }

        await _mutationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = _store;
            if (snapshot.TryGetValue(definition.Tenant, out var byId) &&
                byId.TryGetValue(definition.Id, out var byVersion) &&
                byVersion.TryGetValue(definition.Version, out var existing))
            {
                if (IsIdenticalPublishedRetry(existing, definition))
                {
                    return existing;
                }

                throw new FormDefinitionConflictException(definition.Id, definition.Version, definition.Tenant);
            }

            ValidateLineage(snapshot, definition);
            FormDefinitionValidation.ValidateRecordTypeContinuityOrThrow(definition, Revisions(snapshot, definition.Tenant, definition.Id));
            var publishedAt = _time.GetUtcNow();
            ct.ThrowIfCancellationRequested();
            var published = definition with
            {
                Status = FormDefinitionStatus.Published,
                UpdatedAt = publishedAt,
            };
            _store = MutateStore(snapshot, published);
            return published;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<FormDefinition> CreateAsync(FormDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await ValidateDefinitionAsync(definition, requireSubmitGate: definition.Status == FormDefinitionStatus.Published, ct).ConfigureAwait(false);

        await _mutationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = _store;
            if (snapshot.TryGetValue(definition.Tenant, out var byId) && byId.ContainsKey(definition.Id))
            {
                throw new FormDefinitionConflictException(definition.Id, definition.Version, definition.Tenant);
            }

            if (definition.Lineage is not null)
            {
                throw new FormDefinitionValidationException(
                    definition.Id,
                    "the first revision of a definition id cannot carry lineage; use RegisterAsync for a derived revision.");
            }

            _store = MutateStore(snapshot, definition);
            return definition;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask<FormDefinition> PublishAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default)
        => TransitionAsync(tenant, id, version, FormDefinitionStatus.Published, allowedFrom: new[] { FormDefinitionStatus.Draft, FormDefinitionStatus.Published }, ct);

    /// <inheritdoc />
    public ValueTask<FormDefinition> DeprecateAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default)
        => TransitionAsync(tenant, id, version, FormDefinitionStatus.Deprecated, allowedFrom: new[] { FormDefinitionStatus.Published, FormDefinitionStatus.Deprecated }, ct);

    /// <inheritdoc />
    public ValueTask<FormDefinition> WithdrawAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default)
        => TransitionAsync(tenant, id, version, FormDefinitionStatus.Withdrawn, allowedFrom: new[] { FormDefinitionStatus.Draft, FormDefinitionStatus.Published, FormDefinitionStatus.Deprecated, FormDefinitionStatus.Withdrawn }, ct);

    /// <inheritdoc />
    public async ValueTask<FormDefinition> RestorePackProjectionAsync(
        TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default)
    {
        var existing = await GetAsync(tenant, id, version, ct).ConfigureAwait(false);
        if (existing.Owner != IdentityRef.System)
        {
            throw new InvalidOperationException(
                $"Only a System-owned form projection can be restored; '{id}' v{version} is owned by {existing.Owner}.");
        }

        return await TransitionAsync(
            tenant, id, version, FormDefinitionStatus.Published,
            allowedFrom: new[] { FormDefinitionStatus.Withdrawn, FormDefinitionStatus.Published }, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FormDefinition> ListByTenantAsync(TenantId tenant, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var snapshot = _store;
        if (!snapshot.TryGetValue(tenant, out var byId))
        {
            yield break;
        }

        foreach (var idEntry in byId.OrderBy(kv => kv.Key.Value, StringComparer.Ordinal))
        {
            foreach (var versionEntry in idEntry.Value.OrderBy(kv => kv.Key))
            {
                ct.ThrowIfCancellationRequested();
                yield return versionEntry.Value;
                await Task.Yield();
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FormDefinition> ListCurrentPublishedByTenantAsync(
        TenantId tenant,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var snapshot = _store;
        if (!snapshot.TryGetValue(tenant, out var byId))
        {
            yield break;
        }

        foreach (var idEntry in byId.OrderBy(kv => kv.Key.Value, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var current = idEntry.Value.Values
                .Where(candidate => candidate.Status == FormDefinitionStatus.Published)
                .OrderByDescending(candidate => candidate.Version)
                .FirstOrDefault();
            if (current is not null)
            {
                yield return current;
                await Task.Yield();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _mutationLock.Dispose();

    /// <summary>
    /// Test-only seam: inserts <paramref name="definition"/> as-is, bypassing
    /// <see cref="ValidateDefinitionAsync"/>. Production writes always validate; this exists solely so
    /// tests can simulate a row already persisted before submit_gate became required at publish
    /// (T-756) — data this package's own write paths can no longer produce.
    /// </summary>
    internal void SeedLegacyRevisionForTesting(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _store = MutateStore(_store, definition);
    }

    private async ValueTask<FormDefinition> TransitionAsync(
        TenantId tenant,
        FormDefinitionId id,
        SemanticVersion version,
        FormDefinitionStatus target,
        FormDefinitionStatus[] allowedFrom,
        CancellationToken ct)
    {
        // The record type policy is an external read, so it is resolved before the mutation lock is taken. A revision's
        // content is immutable per (tenant, id, version); only its status changes, so the resolution stays valid.
        // The producers' declaration (K9) is keyed by tenant and form id only, so it is resolved the same way.
        FormDefinition? preValidated = null;
        RecordTypeCreationGate? preResolved = null;
        var declarationResolved = false;
        string? preDeclared = null;
        if (target == FormDefinitionStatus.Published
            && Find(_store, tenant, id, version) is { } candidate
            && candidate.Status != target
            && allowedFrom.Contains(candidate.Status)
            && PassesSynchronousValidation(candidate))
        {
            preDeclared = await ResolveDeclaredCreationAsync(candidate, ct).ConfigureAwait(false);
            declarationResolved = true;
            FormDefinitionValidation.ValidateDeclaredCreationOrThrow(candidate, preDeclared);
            if (candidate.RecordTypeId is not null)
            {
                preResolved = await ResolvePublicationGateAsync(candidate, ct).ConfigureAwait(false);
                preValidated = candidate;
            }
        }

        await _mutationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = _store;
            if (!snapshot.TryGetValue(tenant, out var byId) ||
                !byId.TryGetValue(id, out var byVersion) ||
                !byVersion.TryGetValue(version, out var existing))
            {
                throw new FormDefinitionNotFoundException(id, version, tenant);
            }

            if (!allowedFrom.Contains(existing.Status))
            {
                throw new InvalidOperationException(
                    $"FormDefinition '{id}' v{version} cannot transition from {existing.Status} to {target}; allowed source statuses are [{string.Join(", ", allowedFrom)}].");
            }

            if (target == FormDefinitionStatus.Published)
            {
                var publishing = existing.Status != target;
                ValidateDefinition(existing, requireSubmitGate: publishing);
                if (publishing)
                    FormDefinitionValidation.ValidateDeclaredCreationOrThrow(existing,
                        declarationResolved ? preDeclared : await ResolveDeclaredCreationAsync(existing, ct).ConfigureAwait(false));
                if (publishing && existing.RecordTypeId is not null)
                {
                    // A concurrent registration can make the revision visible only now; resolve it here in that rare case.
                    RecordTypeCreationGate gate;
                    if (preValidated is not null
                        && StringComparer.Ordinal.Equals(preValidated.RecordTypeId, existing.RecordTypeId)
                        && preResolved is { } resolved)
                        gate = resolved;
                    else
                        gate = await ResolvePublicationGateAsync(existing, ct).ConfigureAwait(false);
                    FormDefinitionValidation.ValidateRecordTypePublicationOrThrow(existing, gate);
                }

                if (publishing)
                    FormDefinitionValidation.ValidateRecordTypeContinuityOrThrow(existing, byVersion.Values);
            }

            if (existing.Status == target)
            {
                return existing;
            }

            var transitioned = existing with
            {
                Status = target,
                UpdatedAt = _time.GetUtcNow(),
            };

            _store = MutateStore(snapshot, transitioned);
            return transitioned;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private static Dictionary<TenantId, Dictionary<FormDefinitionId, Dictionary<SemanticVersion, FormDefinition>>> MutateStore(
        Dictionary<TenantId, Dictionary<FormDefinitionId, Dictionary<SemanticVersion, FormDefinition>>> snapshot,
        FormDefinition definition)
    {
        var rebuilt = new Dictionary<TenantId, Dictionary<FormDefinitionId, Dictionary<SemanticVersion, FormDefinition>>>(snapshot);
        if (!rebuilt.TryGetValue(definition.Tenant, out var byId))
        {
            byId = new Dictionary<FormDefinitionId, Dictionary<SemanticVersion, FormDefinition>>();
            rebuilt[definition.Tenant] = byId;
        }
        else
        {
            byId = new Dictionary<FormDefinitionId, Dictionary<SemanticVersion, FormDefinition>>(byId);
            rebuilt[definition.Tenant] = byId;
        }

        if (!byId.TryGetValue(definition.Id, out var byVersion))
        {
            byVersion = new Dictionary<SemanticVersion, FormDefinition>();
            byId[definition.Id] = byVersion;
        }
        else
        {
            byVersion = new Dictionary<SemanticVersion, FormDefinition>(byVersion);
            byId[definition.Id] = byVersion;
        }

        byVersion[definition.Version] = definition;
        return rebuilt;
    }

    // requireSubmitGate is true exactly when the definition is being made Published; the record type target's
    // creation policy is resolved only then, so a Draft saves without a Records lookup.
    private async ValueTask ValidateDefinitionAsync(FormDefinition definition, bool requireSubmitGate, CancellationToken ct)
    {
        ValidateDefinition(definition, requireSubmitGate);
        if (requireSubmitGate)
            FormDefinitionValidation.ValidateDeclaredCreationOrThrow(
                definition, await ResolveDeclaredCreationAsync(definition, ct).ConfigureAwait(false));
        if (requireSubmitGate && definition.RecordTypeId is not null)
            FormDefinitionValidation.ValidateRecordTypePublicationOrThrow(
                definition, await ResolvePublicationGateAsync(definition, ct).ConfigureAwait(false));
    }

    private void ValidateDefinition(FormDefinition definition, bool requireSubmitGate)
    {
        FormDefinitionValidation.ValidateOverlayOrThrow(definition);
        FormDefinitionValidation.ValidateSchemaRefOrThrow(definition);
        FormDefinitionAuthoringValidation.ValidateOrThrow(definition);
        FormDefinitionValidation.ValidateSubmitGateOrThrow(definition, _capabilities, requireSubmitGate);
        FormDefinitionValidation.ValidateRecordTypeTargetOrThrow(definition);
    }

    private bool PassesSynchronousValidation(FormDefinition definition)
    {
        try
        {
            ValidateDefinition(definition, requireSubmitGate: true);
            return true;
        }
        catch (FormDefinitionValidationException)
        {
            return false;
        }
    }

    // K6: the host reader owns every default; a missing reader, a failing reader, an unknown type or an unknown value
    // refuses publication with a stable code at the target's pointer.
    private async ValueTask<RecordTypeCreationGate> ResolvePublicationGateAsync(FormDefinition definition, CancellationToken ct)
    {
        try
        {
            return await RecordTypeCreationPolicy
                .ResolveAsync(_recordTypePolicies, definition.Tenant, definition.RecordTypeId!, ct).ConfigureAwait(false);
        }
        catch (RecordTypeCreationPolicyException refusal)
        {
            throw new FormDefinitionValidationException(definition.Id, refusal.Message, refusal.Code, "/record_type_id");
        }
    }

    // K9: a failing or ambiguous declaration refuses publication with a stable code at the target's pointer.
    private async ValueTask<string?> ResolveDeclaredCreationAsync(FormDefinition definition, CancellationToken ct)
    {
        try
        {
            return await RecordCreationDeclarations
                .ResolveAsync(_recordCreations, definition.Tenant, definition.Id, ct).ConfigureAwait(false);
        }
        catch (RecordTypeCreationPolicyException refusal)
        {
            throw new FormDefinitionValidationException(definition.Id, refusal.Message, refusal.Code, "/record_type_id");
        }
    }

    private static FormDefinition? Find(
        Dictionary<TenantId, Dictionary<FormDefinitionId, Dictionary<SemanticVersion, FormDefinition>>> snapshot,
        TenantId tenant, FormDefinitionId id, SemanticVersion version) =>
        snapshot.TryGetValue(tenant, out var byId) && byId.TryGetValue(id, out var byVersion)
            && byVersion.TryGetValue(version, out var definition) ? definition : null;

    private static IEnumerable<FormDefinition> Revisions(
        Dictionary<TenantId, Dictionary<FormDefinitionId, Dictionary<SemanticVersion, FormDefinition>>> snapshot,
        TenantId tenant, FormDefinitionId id) =>
        snapshot.TryGetValue(tenant, out var byId) && byId.TryGetValue(id, out var byVersion)
            ? byVersion.Values : Enumerable.Empty<FormDefinition>();

    private static bool IsIdenticalPublishedRetry(
        FormDefinition existing,
        FormDefinition requestedDraft)
    {
        if (existing.Status != FormDefinitionStatus.Published)
        {
            return false;
        }

        var expectedPublished = requestedDraft with
        {
            Status = FormDefinitionStatus.Published,
            UpdatedAt = existing.UpdatedAt,
        };

        return JsonNode.DeepEquals(
            JsonSerializer.SerializeToNode(existing),
            JsonSerializer.SerializeToNode(expectedPublished));
    }

    private static void ValidateLineage(
        Dictionary<TenantId, Dictionary<FormDefinitionId, Dictionary<SemanticVersion, FormDefinition>>> snapshot,
        FormDefinition definition)
    {
        if (definition.Lineage is not { } lineage) return;
        if (!snapshot.TryGetValue(definition.Tenant, out var lineageById) ||
            !lineageById.TryGetValue(lineage.ParentDefinitionId, out var lineageByVersion) ||
            !lineageByVersion.ContainsKey(lineage.ParentVersion))
        {
            throw new FormDefinitionValidationException(
                definition.Id,
                $"lineage references parent '{lineage.ParentDefinitionId}' at version '{lineage.ParentVersion}' which is not registered in tenant '{definition.Tenant}'.");
        }
    }
}
