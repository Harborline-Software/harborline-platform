using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.Forms.Models;

/// <summary>
/// <b>Canonical foundation-tier dynamic-forms keystone</b> (ADR 0055).
/// A <see cref="FormDefinition"/> is the single load-bearing record on which
/// the entire dynamic-forms substrate composes — every other surface
/// (the entity instance store, the form engine, the rule evaluator, the
/// CRDT sync substrate, the audit trail, the admin authoring UX) binds to
/// this type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Composition vs. duplication.</b> A form definition is fundamentally
/// a JSON Schema 2020-12 document plus the Forms overlay (sections,
/// rules, permissions, i18n) plus lifecycle metadata. The kernel-tier
/// <c>Harborline.Kernel.SchemaValidation.Schema</c> (in <c>kernel-schema-registry</c>)
/// is the content-addressed canonicalization of the raw JSON Schema
/// document; the keystone <see cref="FormDefinition"/> is the higher-level
/// composite that adds everything beyond pure structural validation.
/// The keystone does <i>not</i> inline the JSON Schema body — it carries
/// a content-addressed <see cref="SchemaRef"/> into the kernel registry
/// (ADR 0055 OQ-3, dual-council ratified). This preserves the
/// content-addressing / TOCTOU-immutability guarantee (INV-S5) and keeps
/// the kernel registry the single source of truth for schema bodies; the
/// form-definition type carries the higher-level overlay + lifecycle data,
/// not a duplicate copy of the document.
/// </para>
/// <para>
/// <b>Identity is the tuple (Id, Version).</b> A definition id is reusable
/// across versions; each version is a distinct, immutable record. The
/// store indexes by id and returns the highest <see cref="Version"/>
/// at status <see cref="FormDefinitionStatus.Published"/> when no specific
/// version is requested.
/// </para>
/// <para>
/// <b>Tenant isolation.</b> Every definition is tenant-scoped via
/// <see cref="Tenant"/>. The store enforces that lookups, listings,
/// and lineage references all stay within a tenant boundary;
/// cross-tenant definition sharing (the marketplace v2 ambition in ADR 0055
/// §"Revisit triggers") is explicitly out of scope for the keystone.
/// </para>
/// <para>
/// <b>Audit emission.</b> The store emits a kernel-audit record for
/// every lifecycle transition (Register / Publish / Deprecate / Withdraw)
/// per ADR 0055 §"Trust impact" + ADR 0049. The audit event types ship
/// with the rule-engine + entity-store PRs that follow the keystone —
/// the keystone records the lifecycle data so those audit emissions have
/// something to read; it does NOT itself emit audit events (no
/// kernel-audit reference on the keystone csproj, to keep the keystone
/// composable in test contexts that do not stand up audit infrastructure).
/// </para>
/// </remarks>
/// <param name="Id">Definition id (reusable across versions; the
/// <c>(Id, Version)</c> tuple is unique).</param>
/// <param name="Version">Semantic version of this revision.</param>
/// <param name="Status">Lifecycle status. New definitions typically register
/// as <see cref="FormDefinitionStatus.Draft"/> and transition to
/// <see cref="FormDefinitionStatus.Published"/> via the store's
/// <c>PublishAsync</c> call.</param>
/// <param name="Tenant">Owning tenant; the store enforces tenant isolation
/// on all lookups.</param>
/// <param name="Owner">Identity reference for the authoring principal
/// (typically the tenant admin who registered the definition; for seed data
/// use <see cref="IdentityRef.System"/>).</param>
/// <param name="SchemaRef">Content-addressed reference into the kernel
/// schema registry (<c>Harborline.Kernel.SchemaValidation.ISchemaRegistry</c>) for this
/// definition's underlying JSON Schema 2020-12 document. The keystone
/// carries the reference, not the document body — the kernel registry is
/// the single source of truth for schema bodies (content-addressed,
/// immutable, validated at registration there). Per ADR 0055 OQ-3, dual-
/// council ratified, this preserves INV-S5 (content-addressing /
/// TOCTOU-immutability) and avoids re-authoring the kernel registry's job.</param>
/// <param name="Overlay">The Forms overlay — fields, sections, rules,
/// i18n. The store enforces overlay invariants (every field referenced
/// by a section appears in <see cref="HarborlineOverlay.Fields"/>; section
/// and rule ids are unique; etc.).</param>
/// <param name="Lineage">Optional extension lineage (this definition extends
/// another definition). Composed with ADR 0005 type customization.</param>
/// <param name="CreatedAt">UTC timestamp at which this revision was first
/// registered.</param>
/// <param name="UpdatedAt">UTC timestamp at which this revision's
/// <see cref="Status"/> was last transitioned. Equals
/// <see cref="CreatedAt"/> on a freshly-registered revision.</param>
/// <param name="Authoring">Optional immutable Harborline App authoring metadata that cannot be
/// reconstructed exactly from the schema and runtime overlay.</param>
public sealed record FormDefinition(
    FormDefinitionId Id,
    SemanticVersion Version,
    FormDefinitionStatus Status,
    TenantId Tenant,
    IdentityRef Owner,
    SchemaId SchemaRef,
    HarborlineOverlay Overlay,
    FormDefinitionLineage? Lineage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    FormDefinitionAuthoring? Authoring = null);
