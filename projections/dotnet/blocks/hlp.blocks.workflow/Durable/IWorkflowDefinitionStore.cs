using System.Text.Json;

namespace Harborline.Blocks.Workflow.Durable;

// ─────────────────────────────────────────────────────────────────────────────
//  WF-KEY (W-7 / G5) — the DURABLE workflow-definition STORE seam. The process
//  analog of Foundation.Forms' IFormDefinitionStore: SAVE + LOAD + LIST + PUBLISH
//  the authored WorkflowDefinition a tenant admin builds in the Harborline App workflow
//  builder. STORAGE only — a definition persisted here is authored + admitted +
//  durable; it is NOT executed (the general A1 interpreter stays gated on the
//  broker-PEP, ADR 0143). Persisting an authored+admitted definition is safe +
//  ungated.
//
//  Fail-closed admission at persist: RegisterAsync runs the shipped
//  WorkflowAdmissionValidator on the derived model BEFORE the store write — the
//  process analog of the forms store's ValidateOverlayOrThrow at RegisterAsync. An
//  inadmissible definition (unclassified action, or a CP action reachable from an
//  autonomous trigger with no interposed human-task) is REJECTED at persist with a
//  stable code (WorkflowAdmissionException.Result), before anything is written.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One persisted revision of a workflow definition. <see cref="Authored"/> is the
/// FULL authoring JSON (the <c>@harborline-software/contracts</c> <c>WorkflowDefinition</c> the
/// Harborline App PUT, with display labels/title) persisted verbatim so the builder reloads
/// intact; the lean <see cref="WorkflowDefinition"/> admission model is derived from
/// it at persist and is not what is stored (it carries no display chrome).
/// </summary>
/// <param name="Tenant">Owning tenant id (server-resolved; never trusted from the client).</param>
/// <param name="Key">Definition key.</param>
/// <param name="Version">Canonical "{major}.{minor}.{patch}" — the server-minted version.</param>
/// <param name="Status">Lifecycle status of this revision.</param>
/// <param name="Authored">The authored definition JSON, persisted verbatim for the round-trip.</param>
public sealed record WorkflowDefinitionRecord(
    string Tenant,
    string Key,
    string Version,
    WorkflowDefinitionStatus Status,
    JsonElement Authored);

/// <summary>
/// The AUTHORING face of the durable workflow-definition store (WF-KEY W-7). The process analog of
/// <c>IFormDefinitionStore</c>: definitions are immutable per <c>(tenant, key, version)</c>; a re-save mints
/// the next patch version. Its reads are the LENIENT builder-reload reads
/// (<see cref="GetCurrentPublishedAsync"/> / <see cref="GetAsync"/>) — they hand back the persisted authored
/// definition as-is for editing.
/// <para>
/// <b>Authoring vs execution (SC2 F-2).</b> This face must NOT be used to load a definition FOR EXECUTION —
/// its reads do not re-admit against the current capability registry. An interpreter / instantiation /
/// D7-re-pin path injects <see cref="IWorkflowDefinitionExecutionStore"/> instead, whose reads are
/// fail-closed re-validating. Splitting the faces makes the safe (re-validating) path the ONLY path an
/// execution consumer can reach.
/// </para>
/// </summary>
public interface IWorkflowDefinitionStore
{
    /// <summary>
    /// Admits <paramref name="model"/> (fail-closed — throws
    /// <see cref="WorkflowAdmissionException"/> BEFORE any write) then persists
    /// <paramref name="authored"/> as a new Draft revision keyed by the model's
    /// <c>(tenant, key, version)</c>. Rejects a duplicate revision with
    /// <see cref="WorkflowDefinitionConflictException"/>.
    /// </summary>
    ValueTask<WorkflowDefinitionRecord> RegisterAsync(
        WorkflowDefinition model, JsonElement authored, CancellationToken ct = default);

    /// <summary>The highest-version Published revision of a definition, or null when none is published.</summary>
    ValueTask<WorkflowDefinitionRecord?> GetCurrentPublishedAsync(
        string tenant, string key, CancellationToken ct = default);

    /// <summary>An exact <c>(tenant, key, version)</c> revision; throws <see cref="WorkflowDefinitionNotFoundException"/>.</summary>
    ValueTask<WorkflowDefinitionRecord> GetAsync(
        string tenant, string key, string version, CancellationToken ct = default);

    /// <summary>Transitions a Draft (or already-Published — a no-op) revision to Published.</summary>
    ValueTask<WorkflowDefinitionRecord> PublishAsync(
        string tenant, string key, string version, CancellationToken ct = default);

    /// <summary>Transitions a revision to Withdrawn, preserving its authored bytes.</summary>
    ValueTask<WorkflowDefinitionRecord> WithdrawAsync(
        string tenant, string key, string version, CancellationToken ct = default);

    /// <summary>
    /// Restores a matching System-owned, Pack-provenance projection from Withdrawn to Published.
    /// This narrow reverse-projector seam does not relax ordinary forward-only publication.
    /// </summary>
    ValueTask<WorkflowDefinitionRecord> RestorePackProjectionAsync(
        string tenant, string key, string version, CancellationToken ct = default);

    /// <summary>All of a tenant's revisions, ordered <c>(key asc, version asc)</c>.</summary>
    IAsyncEnumerable<WorkflowDefinitionRecord> ListByTenantAsync(
        string tenant, CancellationToken ct = default);
}

/// <summary>
/// The EXECUTION face of the durable workflow-definition store (SC2 F-2 / ADR 0135 A1 R-1 / ADR 0143 R1-E
/// DoD #4). Exposes ONLY the fail-closed, load-time re-validating reads: every definition handed out here is
/// re-admitted against the CURRENT capability registry at the moment of load, so a now-inadmissible
/// definition — one reclassified (a capability became CP), edited to route a CP edge around the human-task,
/// tampered in storage, or persisted before this gate existed — is REJECTED (throws
/// <see cref="WorkflowAdmissionException"/>) before it can execute.
/// <para>
/// An interpreter / instantiation / D7-re-pin path injects THIS interface, never
/// <see cref="IWorkflowDefinitionStore"/> — because this face has NO lenient read, "load a definition for
/// execution" can only traverse the re-validating path. That is the F-2 fix: the safe path is the only path
/// a DI-resolved execution consumer can reach (the concrete store implements both faces, but the abstraction
/// an executor is handed exposes only the gated reads).
/// </para>
/// </summary>
public interface IWorkflowDefinitionExecutionStore
{
    /// <summary>
    /// LOAD-FOR-EXECUTION: the highest-version Published revision of a definition, RE-ADMITTED at load.
    /// Returns <see langword="null"/> when nothing is published; throws <see cref="WorkflowAdmissionException"/>
    /// if the persisted definition is no longer admissible (fail-closed — it must not execute).
    /// </summary>
    ValueTask<WorkflowDefinitionRecord?> GetAdmittedCurrentPublishedAsync(
        string tenant, string key, CancellationToken ct = default);

    /// <summary>
    /// LOAD-FOR-EXECUTION: an exact Published <c>(tenant, key, version)</c> revision (the D7-pinned version an
    /// instance resolves replay against), RE-ADMITTED at load. Throws
    /// <see cref="WorkflowDefinitionNotFoundException"/> if absent or not Published,
    /// <see cref="WorkflowAdmissionException"/> if now-inadmissible.
    /// </summary>
    ValueTask<WorkflowDefinitionRecord> GetAdmittedAsync(
        string tenant, string key, string version, CancellationToken ct = default);
}

/// <summary>Raised when a definition is not found (or is cross-tenant — indistinguishable, INV-S1).</summary>
public sealed class WorkflowDefinitionNotFoundException(string key, string version, string tenant)
    : InvalidOperationException($"No workflow definition '{key}' v{version} for tenant '{tenant}'.")
{
    /// <summary>The requested definition key.</summary>
    public string Key { get; } = key;

    /// <summary>The requested version.</summary>
    public string Version { get; } = version;

    /// <summary>The requesting tenant.</summary>
    public string Tenant { get; } = tenant;
}

/// <summary>Raised when re-registering an existing <c>(tenant, key, version)</c> (revisions are immutable).</summary>
public sealed class WorkflowDefinitionConflictException(string key, string version, string tenant)
    : InvalidOperationException(
        $"A revision of workflow definition '{key}' at version {version} already exists for tenant '{tenant}'.")
{
    /// <summary>The conflicting definition key.</summary>
    public string Key { get; } = key;

    /// <summary>The conflicting version.</summary>
    public string Version { get; } = version;

    /// <summary>The tenant.</summary>
    public string Tenant { get; } = tenant;
}
