using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harborline.Blocks.Workflow.Durable;

/// <summary>
/// Durable-seam <see cref="IWorkflowDefinitionStore"/> backed by an in-process, thread-safe
/// revision map — the Harborline replacement for the earlier source EntityStore-backed store at the
/// narrowed definition-store port. Every caller-observable behavior of the port is preserved:
/// fail-closed admission at register, immutable <c>(tenant, key, version)</c> revisions,
/// highest-published-version resolution, tenant isolation, stable listing order, the
/// pack-provenance restore seam, and the fail-closed re-admitting execution face.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fail-closed admission (the security keystone of the port).</b> <see cref="RegisterAsync"/>
/// runs the shipped <see cref="IWorkflowAdmissionValidator"/> (registry-derived classification)
/// BEFORE the store write. An inadmissible definition throws
/// <see cref="WorkflowAdmissionException"/> and NOTHING is persisted. Because the gate lives in
/// the store (not only a transport route), every persist path is fail-closed.
/// </para>
/// <para>
/// <b>Envelope.</b> The stored body is a query-stable envelope (<c>kind</c>, <c>tenant</c>,
/// <c>key</c>, <c>version</c>, <c>status</c>) with the full authored definition under
/// <c>authored</c>, mirroring the seam contract: the lean admission model is used only to derive
/// the keys + run admission; persisting <c>authored</c> verbatim is what round-trips the builder
/// intact.
/// </para>
/// <para>
/// <b>Durability posture.</b> This impl is in-process: a host that needs cross-restart
/// durability registers its own store behind the same two faces. The port contract — not the
/// backing — is what the engine composes, exactly the <see cref="IWorkflowStore"/> seam split.
/// </para>
/// </remarks>
public sealed class InMemoryWorkflowDefinitionStore : IWorkflowDefinitionStore, IWorkflowDefinitionExecutionStore
{
    private const string EnvelopeKind = "workflow-definition";

    private readonly object _gate = new();
    private readonly Dictionary<(string Tenant, string Key, string Version), JsonDocument> _envelopes = new();

    private readonly IWorkflowAdmissionValidator _admission;

    /// <summary>Constructs the store over an explicit admission validator (DI/test seam).</summary>
    public InMemoryWorkflowDefinitionStore(IWorkflowAdmissionValidator admission)
        => _admission = admission ?? throw new ArgumentNullException(nameof(admission));

    /// <summary>Constructs the store using the canonical admission validator.</summary>
    public InMemoryWorkflowDefinitionStore()
        : this(new WorkflowAdmissionValidator())
    {
    }

    /// <inheritdoc />
    public ValueTask<WorkflowDefinitionRecord> RegisterAsync(
        WorkflowDefinition model, JsonElement authored, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Fail-closed admission BEFORE the write. Throws WorkflowAdmissionException with the surviving
        // violations; nothing is persisted.
        _admission.EnsureAdmissible(model);

        var tenant = model.Tenant;
        var key = model.Key;
        var version = model.Version;

        lock (_gate)
        {
            // Immutable revisions: reject re-registration of an existing (tenant, key, version).
            if (_envelopes.ContainsKey((tenant, key, version)))
                throw new WorkflowDefinitionConflictException(key, version, tenant);

            _envelopes[(tenant, key, version)] =
                SerializeEnvelope(tenant, key, version, WorkflowDefinitionStatus.Draft, authored);
        }

        return ValueTask.FromResult(
            new WorkflowDefinitionRecord(tenant, key, version, WorkflowDefinitionStatus.Draft, authored.Clone()));
    }

    /// <inheritdoc />
    public ValueTask<WorkflowDefinitionRecord?> GetCurrentPublishedAsync(
        string tenant, string key, CancellationToken ct = default)
    {
        lock (_gate)
        {
            WorkflowDefinitionRecord? best = null;
            foreach (var ((rowTenant, rowKey, _), envelope) in _envelopes)
            {
                if (rowTenant != tenant || rowKey != key)
                    continue;
                var candidate = ToRecord(envelope);
                if (candidate.Status != WorkflowDefinitionStatus.Published)
                    continue;
                if (best is null || CompareVersions(candidate.Version, best.Version) > 0)
                    best = candidate;
            }
            return ValueTask.FromResult(best);
        }
    }

    /// <inheritdoc />
    public ValueTask<WorkflowDefinitionRecord> GetAsync(
        string tenant, string key, string version, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_envelopes.TryGetValue((tenant, key, version), out var envelope))
                throw new WorkflowDefinitionNotFoundException(key, version, tenant);
            return ValueTask.FromResult(ToRecord(envelope));
        }
    }

    /// <summary>
    /// LOAD-FOR-EXECUTION path. Loads the highest-version Published revision AND re-runs admission on
    /// its persisted <c>authored</c> JSON — fail-closed: a definition that was admitted under an older
    /// capability registry but is NO LONGER admissible (e.g. a capability was reclassified to CP, or an
    /// edit routed a CP edge around the human-task) throws <see cref="WorkflowAdmissionException"/> and
    /// MUST NOT be handed out for execution. Returns <see langword="null"/> when nothing is published.
    /// Distinct from <see cref="GetCurrentPublishedAsync"/> (the lenient builder-reload read).
    /// </summary>
    public async ValueTask<WorkflowDefinitionRecord?> GetAdmittedCurrentPublishedAsync(
        string tenant, string key, CancellationToken ct = default)
    {
        var record = await GetCurrentPublishedAsync(tenant, key, ct).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        // Re-validate the PERSISTED authored JSON with the SAME canonical parse + admission validator the
        // register-time path used (no drift). Throws WorkflowAdmissionException if now-inadmissible.
        _ = new WorkflowDefinitionLoadValidator(_admission)
            .ReadAdmissibleOrThrow(record.Authored, record.Tenant, record.Key, record.Version);
        return record;
    }

    /// <summary>
    /// LOAD-FOR-EXECUTION path for an exact <c>(tenant, key, version)</c> revision — the pinned version
    /// an instance resolves replay against. Loads the revision AND re-runs admission (fail-closed, as
    /// <see cref="GetAdmittedCurrentPublishedAsync"/>). Throws
    /// <see cref="WorkflowDefinitionNotFoundException"/> if absent or not Published,
    /// <see cref="WorkflowAdmissionException"/> if now-inadmissible.
    /// </summary>
    public async ValueTask<WorkflowDefinitionRecord> GetAdmittedAsync(
        string tenant, string key, string version, CancellationToken ct = default)
    {
        var record = await GetAsync(tenant, key, version, ct).ConfigureAwait(false);
        if (record.Status != WorkflowDefinitionStatus.Published)
        {
            throw new WorkflowDefinitionNotFoundException(key, version, tenant);
        }
        _ = new WorkflowDefinitionLoadValidator(_admission)
            .ReadAdmissibleOrThrow(record.Authored, record.Tenant, record.Key, record.Version);
        return record;
    }

    /// <inheritdoc />
    public async ValueTask<WorkflowDefinitionRecord> PublishAsync(
        string tenant, string key, string version, CancellationToken ct = default)
    {
        var existing = await GetAsync(tenant, key, version, ct).ConfigureAwait(false);
        if (existing.Status is not (WorkflowDefinitionStatus.Draft or WorkflowDefinitionStatus.Published))
        {
            throw new InvalidOperationException(
                $"WorkflowDefinition '{key}' v{version} cannot transition from {existing.Status} to Published.");
        }

        return Transition(existing, WorkflowDefinitionStatus.Published);
    }

    /// <inheritdoc />
    public async ValueTask<WorkflowDefinitionRecord> WithdrawAsync(
        string tenant, string key, string version, CancellationToken ct = default)
    {
        var existing = await GetAsync(tenant, key, version, ct).ConfigureAwait(false);
        return Transition(existing, WorkflowDefinitionStatus.Withdrawn);
    }

    /// <inheritdoc />
    public async ValueTask<WorkflowDefinitionRecord> RestorePackProjectionAsync(
        string tenant, string key, string version, CancellationToken ct = default)
    {
        var existing = await GetAsync(tenant, key, version, ct).ConfigureAwait(false);
        if (!IsSystemPackProjection(existing.Authored))
        {
            throw new InvalidOperationException(
                $"Only a System-owned, Pack-provenance workflow projection can be restored; '{key}' v{version} is not one.");
        }
        if (existing.Status is not (WorkflowDefinitionStatus.Withdrawn or WorkflowDefinitionStatus.Published))
        {
            throw new InvalidOperationException(
                $"WorkflowDefinition '{key}' v{version} cannot be restored from {existing.Status}.");
        }
        _ = new WorkflowDefinitionLoadValidator(_admission)
            .ReadAdmissibleOrThrow(existing.Authored, existing.Tenant, existing.Key, existing.Version);

        return Transition(existing, WorkflowDefinitionStatus.Published);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<WorkflowDefinitionRecord> ListByTenantAsync(
        string tenant, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        List<WorkflowDefinitionRecord> records;
        lock (_gate)
        {
            records = _envelopes
                .Where(pair => pair.Key.Tenant == tenant)
                .Select(pair => ToRecord(pair.Value))
                .ToList();
        }

        // Stable order: (key ordinal asc, version asc) — the port's contractually stable listing order.
        records.Sort(static (a, b) =>
        {
            var byKey = string.CompareOrdinal(a.Key, b.Key);
            return byKey != 0 ? byKey : CompareVersions(a.Version, b.Version);
        });

        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            yield return record;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private WorkflowDefinitionRecord Transition(WorkflowDefinitionRecord existing, WorkflowDefinitionStatus target)
    {
        if (existing.Status == target)
        {
            return existing;
        }

        lock (_gate)
        {
            _envelopes[(existing.Tenant, existing.Key, existing.Version)] =
                SerializeEnvelope(existing.Tenant, existing.Key, existing.Version, target, existing.Authored);
        }
        return existing with { Status = target };
    }

    /// <summary>
    /// The Harborline pack-provenance sentinel: a System-owned (<c>system:__harborline</c>) authored
    /// definition whose <c>provenance</c> is <c>Pack</c>. Only such a projection can traverse the narrow
    /// reverse-projector restore seam; ordinary forward-only publication is unaffected.
    /// </summary>
    private static bool IsSystemPackProjection(JsonElement authored)
    {
        if (!authored.TryGetProperty("provenance", out var provenance)
            || !string.Equals(provenance.GetString(), "Pack", StringComparison.Ordinal))
        {
            return false;
        }

        return authored.TryGetProperty("owner", out var owner)
            && owner.TryGetProperty("scheme", out var scheme)
            && owner.TryGetProperty("value", out var value)
            && string.Equals(scheme.GetString(), "system", StringComparison.Ordinal)
            && string.Equals(value.GetString(), "__harborline", StringComparison.Ordinal);
    }

    private static JsonDocument SerializeEnvelope(
        string tenant, string key, string version, WorkflowDefinitionStatus status, JsonElement authored)
    {
        var envelope = new JsonObject
        {
            ["kind"] = EnvelopeKind,
            ["tenant"] = tenant,
            ["key"] = key,
            ["version"] = version,
            ["status"] = status.ToString(),
            ["authored"] = JsonNode.Parse(authored.GetRawText()),
        };
        return JsonDocument.Parse(envelope.ToJsonString());
    }

    private static WorkflowDefinitionRecord ToRecord(JsonDocument body)
    {
        var root = body.RootElement;
        var tenant = root.GetProperty("tenant").GetString()
            ?? throw new JsonException("workflow-definition envelope had a null 'tenant'.");
        var key = root.GetProperty("key").GetString()
            ?? throw new JsonException("workflow-definition envelope had a null 'key'.");
        var version = root.GetProperty("version").GetString()
            ?? throw new JsonException("workflow-definition envelope had a null 'version'.");
        var status = Enum.Parse<WorkflowDefinitionStatus>(
            root.GetProperty("status").GetString()
            ?? throw new JsonException("workflow-definition envelope had a null 'status'."));
        // Clone so the returned element outlives the envelope's backing JsonDocument.
        var authored = root.GetProperty("authored").Clone();
        return new WorkflowDefinitionRecord(tenant, key, version, status, authored);
    }

    /// <summary>Compares two "{major}.{minor}.{patch}" version strings; non-numeric parts sort as 0.</summary>
    private static int CompareVersions(string a, string b)
    {
        var (aMajor, aMinor, aPatch) = ParseVersion(a);
        var (bMajor, bMinor, bPatch) = ParseVersion(b);
        if (aMajor != bMajor) return aMajor.CompareTo(bMajor);
        if (aMinor != bMinor) return aMinor.CompareTo(bMinor);
        return aPatch.CompareTo(bPatch);
    }

    private static (int Major, int Minor, int Patch) ParseVersion(string version)
    {
        var parts = version.Split('.');
        int At(int i) => parts.Length > i && int.TryParse(parts[i], out var n) ? n : 0;
        return (At(0), At(1), At(2));
    }
}
